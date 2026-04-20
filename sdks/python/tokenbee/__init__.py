import os
import httpx
from enum import Enum

class CompressionRate(str, Enum):
    LOW = "0.75"
    MEDIUM = "0.5"
    HIGH = "0.33"
    EXTREME = "0.2"

class TokenBeeModel(str, Enum):
    # OpenAI
    OPENAI_GPT_4O = "openai/gpt-4o"
    OPENAI_GPT_4O_MINI = "openai/gpt-4o-mini"
    OPENAI_O1 = "openai/o1"
    OPENAI_O1_MINI = "openai/o1-mini"
    OPENAI_O3_MINI = "openai/o3-mini"

    # Anthropic
    ANTHROPIC_CLAUDE_4_6_SONNET = "anthropic/claude-4-6-sonnet-latest"
    ANTHROPIC_CLAUDE_4_6_OPUS = "anthropic/claude-4-6-opus-latest"
    ANTHROPIC_CLAUDE_3_5_SONNET = "anthropic/claude-3-5-sonnet-latest"
    ANTHROPIC_CLAUDE_3_5_HAIKU = "anthropic/claude-3-5-haiku-latest"
    ANTHROPIC_CLAUDE_3_OPUS = "anthropic/claude-3-opus-latest"

    # Google
    GEMINI_3_1_PRO = "google/gemini-3.1-pro"
    GEMINI_3_1_FLASH = "google/gemini-3.1-flash"
    GEMINI_2_0_FLASH = "google/gemini-2.0-flash"
    GEMINI_2_0_PRO = "google/gemini-2.0-pro-exp"
    GEMINI_1_5_PRO = "google/gemini-1.5-pro"
    GEMINI_1_5_FLASH = "google/gemini-1.5-flash"

    # Mistral
    MISTRAL_LARGE = "mistral/mistral-large-latest"
    MISTRAL_SMALL = "mistral/mistral-small-latest"
    PIXTRAL_LARGE = "mistral/pixtral-large-latest"
    MISTRAL_NEMO = "mistral/open-mistral-nemo"

    # Perplexity
    PERPLEXITY_SONAR = "perplexity/sonar"
    PERPLEXITY_SONAR_PRO = "perplexity/sonar-pro"
    PERPLEXITY_SONAR_REASONING = "perplexity/sonar-reasoning"

    # Groq
    GROQ_LLAMA_3_3_70B = "groq/llama-3.3-70b-versatile"
    GROQ_LLAMA_3_1_8B = "groq/llama-3.1-8b-instant"
    GROQ_MIXTRAL_8X7B = "groq/mixtral-8x7b-32768"
    GROQ_GEMMA2_9B = "groq/gemma2-9b-it"

class TokenBee:
    def __init__(
        self, 
        api_key: str,
        llm_key: str,
        compression: str = "auto", 
        rate: str = CompressionRate.MEDIUM, 
        model: str = "", 
        provider: str = "",
        privacy: bool = False
    ):
        self.api_key = api_key
        self.llm_key = llm_key
        self._base_url = "https://tokenbee.io/v1"
        self.headers = {
            "Authorization": f"Bearer {api_key}",
            "X-TokenBee-Key": api_key,
            "X-TB-Key": api_key,
            "X-LLM-Key": llm_key,
            "X-TokenBee-Compression": compression,
            "X-TokenBee-Rate": rate,
            "X-TokenBee-Privacy": str(privacy).lower()
        }
        if model:
            self.headers["X-TokenBee-Model"] = model
        if provider:
            self.headers["X-TokenBee-Provider"] = provider

        self.client = httpx.Client(headers=self.headers, base_url=self._base_url)

    def send(self, model: str, input: dict):
        parts = model.split("/")
        provider = parts[0] if len(parts) > 1 else self.headers.get("X-TokenBee-Provider", "openai")
        model_name = "/".join(parts[1:]) if len(parts) > 1 else parts[0]

        headers = self.headers.copy()
        headers["X-TokenBee-Model"] = model_name
        headers["X-TokenBee-Provider"] = provider
        
        if "compression" in input:
            headers["X-TokenBee-Compression"] = str(input["compression"])
        if "rate" in input:
            headers["X-TokenBee-Rate"] = str(input["rate"])
        if "privacy" in input:
            headers["X-TokenBee-Privacy"] = str(input["privacy"]).lower()

        payload = input.copy()
        payload["model"] = model_name
        payload.pop("compression", None)
        payload.pop("rate", None)
        payload.pop("privacy", None)

        response = self.client.post("/chat/completions", json=payload, headers=headers)
        response.raise_for_status()
        return response.json()
