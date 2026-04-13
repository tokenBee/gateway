import json
import logging
import math
from contextlib import asynccontextmanager
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from typing import Optional, List

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Global variable to hold our model
llm_lingua = None

@asynccontextmanager
async def lifespan(app: FastAPI):
    global llm_lingua
    logger.info("Loading compression model...")
    from llmlingua import PromptCompressor
    try:
        llm_lingua = PromptCompressor(
            model_name="microsoft/llmlingua-2-bert-base-multilingual-cased-meetingbank",
            use_llmlingua2=True,
            device_map="cpu"
        )
        logger.info("Compression model loaded and ready")
    except Exception as e:
        logger.error(f"Failed to load compression model: {e}")
    yield
    # Cleanup if needed
    llm_lingua = None

app = FastAPI(lifespan=lifespan)

class CompressRequest(BaseModel):
    prompt: str
    rate: float = 0.5
    query: Optional[str] = None
    mode: str = "auto"
    coarse: bool = False

class BatchCompressRequest(BaseModel):
    prompts: List[str]
    rate: float = 0.5
    query: Optional[str] = None
    mode: str = "auto"

@app.get("/health")
async def health():
    if llm_lingua is not None:
        return {"status": "ok", "model": "llmlingua-2"}
    return JSONResponse(status_code=503, content={"status": "loading"})

@app.get("/models")
async def models():
    return {
        "models": [
            {
                "name": "agnostic",
                "description": "Context compression without query",
                "queryRequired": False,
                "default": True
            },
            {
                "name": "query-specific",
                "description": "Context compression using your query",
                "queryRequired": False,
                "queryRecommended": True,
                "default": False
            }
        ]
    }

def process_compression(req: CompressRequest):
    try:
        # Parse the inner JSON string from the .NET proxy
        original_prompt_dict = json.loads(req.prompt)
    except json.JSONDecodeError as e:
        logger.error(f"Failed to decode prompt JSON: {e}")
        return _fallback_response(req.prompt)

    try:
        messages = original_prompt_dict.get("messages", [])
        if not messages:
            return _fallback_response(req.prompt)

        # Extract last user message as auto_query
        auto_query = ""
        auto_query_used = False
        for msg in reversed(messages):
            if msg.get("role") == "user" and isinstance(msg.get("content"), str):
                auto_query = msg.get("content")
                break

        # Determine effective query
        effective_query = ""
        effective_mode = req.mode
        
        if req.query is not None:
            effective_query = req.query
            if req.mode == "auto":
                effective_mode = "query-specific" if effective_query else "agnostic"
        elif req.mode == "query-specific":
            effective_query = auto_query
            auto_query_used = True
        elif req.mode == "auto":
            if auto_query:
                effective_mode = "query-specific"
                effective_query = auto_query
                auto_query_used = True
            else:
                effective_mode = "agnostic"
        
        effective_rate = req.rate
        if effective_rate > 1.0:
            effective_rate = 1.0 / effective_rate
            
        target_token_multiplier = effective_rate
            
        # 1. Identify which messages to compress
        to_compress_indices = []
        texts_to_compress = []
        
        for i, msg in enumerate(messages):
            if msg.get("role") in ["user", "assistant"] and isinstance(msg.get("content"), str):
                to_compress_indices.append(i)
                texts_to_compress.append(msg.get("content"))

        if not texts_to_compress:
            return _fallback_response(req.prompt, "No content to compress")

        try:
            # We estimate the target tokens based on original characters
            original_char_count = sum(len(t) for t in texts_to_compress)
            est_tokens = original_char_count // 4

            full_context = "\n".join(texts_to_compress)
            
            # Coarse auto fallback
            coarse_mode = req.coarse
            if est_tokens > 4000:
                coarse_mode = True

            compressed_text = ""
            if coarse_mode:
                paragraphs = full_context.split("\n\n")
                comp_paragraphs = []
                for p in paragraphs:
                    if not p.strip():
                        continue
                    p_est = len(p) // 4
                    p_target = max(1, int(p_est * target_token_multiplier))
                    if effective_mode == "query-specific" and effective_query:
                        result = llm_lingua.compress_prompt(
                            context=[p],
                            instruction="",
                            question=effective_query,
                            target_token=p_target,
                            rank_method="llmlingua2",
                            context_budget="+50%",
                        )
                    else:
                        result = llm_lingua.compress_prompt(
                            context=[p],
                            instruction="",
                            question="",
                            target_token=p_target,
                            rank_method="llmlingua2",
                        )
                    comp_paragraphs.append(result.get("compressed_prompt", ""))
                compressed_text = "\n\n".join(comp_paragraphs)
            else:
                target = max(1, int(est_tokens * target_token_multiplier))
                if effective_mode == "query-specific" and effective_query:
                    result = llm_lingua.compress_prompt(
                        context=[full_context],
                        instruction="",
                        question=effective_query,
                        target_token=target,
                        rank_method="llmlingua2",
                        context_budget="+50%",
                    )
                else:
                    result = llm_lingua.compress_prompt(
                        context=[full_context],
                        instruction="",
                        question="",
                        target_token=target,
                        rank_method="llmlingua2",
                    )
                
                compressed_text = result.get("compressed_prompt", "")
            
            compressed_messages = []
            compressed_indices_handled = False
            
            for i, msg in enumerate(messages):
                if i in to_compress_indices:
                    if not compressed_indices_handled:
                        new_msg = msg.copy()
                        new_msg["content"] = compressed_text
                        compressed_messages.append(new_msg)
                        compressed_indices_handled = True
                    else:
                        continue
                else:
                    compressed_messages.append(msg)

            compressed_prompt_dict = original_prompt_dict.copy()
            compressed_prompt_dict["messages"] = compressed_messages
            compressed_prompt_str = json.dumps(compressed_prompt_dict)
            
            comp_tokens = len(compressed_text) // 4
            
            return {
                "compressed": compressed_prompt_str,
                "original_tokens": est_tokens,
                "compressed_tokens": comp_tokens,
                "saved_tokens": max(0, est_tokens - comp_tokens),
                "compression_rate": round(comp_tokens / est_tokens if est_tokens > 0 else 1.0, 2),
                "mode_used": effective_mode,
                "query_used": effective_query if effective_mode == "query-specific" else "",
                "auto_query": auto_query_used
            }

        except Exception as inner_e:
            logger.error(f"Model pass failed: {inner_e}")
            return _fallback_response(req.prompt)
        
    except Exception as e:
        logger.error(f"Failed to process compression logic: {e}")
        return _fallback_response(req.prompt)

@app.post("/compress")
async def compress(req: CompressRequest):
    return process_compression(req)

@app.post("/compress/batch")
async def compress_batch(req: BatchCompressRequest):
    if len(req.prompts) > 20:
        return JSONResponse(status_code=400, content={"error": "Max batch size is 20"})
    
    results = []
    for prompt in req.prompts:
        cr = CompressRequest(
            prompt=prompt,
            rate=req.rate,
            query=req.query,
            mode=req.mode
        )
        results.append(process_compression(cr))
    
    return results

def _fallback_response(original_prompt: str, reason: str = ""):
    est = len(original_prompt) // 4
    return {
        "compressed": original_prompt,
        "original_tokens": est,
        "compressed_tokens": est,
        "saved_tokens": 0,
        "compression_rate": 1.0,
        "mode_used": "agnostic",
        "query_used": "",
        "auto_query": False
    }
