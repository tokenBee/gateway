import os
import httpx
from enum import Enum

class CompressionRate(str, Enum):
    LOW = "0.75"
    MEDIUM = "0.5"
    HIGH = "0.33"
    EXTREME = "0.2"

class CompressionStrategy(str, Enum):
    HIVE = "hive_v1"
    SMART = "smart_v1"

class TokenBeeContext(str, Enum):
    AUTO = "auto"
    CONVERSATION = "conversation"
    DOCUMENT = "document"
    AGENT = "agent"
    CODE = "code"

class TokenBeeModel(str, Enum):
    # OpenAI — current GPT-5.6 / GPT-5 family
    OPENAI_GPT_5_6_SOL = "openai/gpt-5.6-sol"
    OPENAI_GPT_5_6_TERRA = "openai/gpt-5.6-terra"
    OPENAI_GPT_5_6_LUNA = "openai/gpt-5.6-luna"
    OPENAI_GPT_5 = "openai/gpt-5"
    OPENAI_GPT_5_MINI = "openai/gpt-5-mini"
    OPENAI_GPT_5_NANO = "openai/gpt-5-nano"
    OPENAI_GPT_5_4 = "openai/gpt-5.4"
    OPENAI_GPT_5_4_MINI = "openai/gpt-5.4-mini"
    OPENAI_GPT_5_4_NANO = "openai/gpt-5.4-nano"
    OPENAI_GPT_4_1 = "openai/gpt-4.1"
    OPENAI_GPT_4_1_MINI = "openai/gpt-4.1-mini"
    OPENAI_GPT_4_1_NANO = "openai/gpt-4.1-nano"
    OPENAI_GPT_4O = "openai/gpt-4o"
    OPENAI_GPT_4O_MINI = "openai/gpt-4o-mini"
    OPENAI_O3 = "openai/o3"
    OPENAI_O3_MINI = "openai/o3-mini"
    OPENAI_O4_MINI = "openai/o4-mini"
    OPENAI_O1 = "openai/o1"
    OPENAI_O1_MINI = "openai/o1-mini"

    # Anthropic
    ANTHROPIC_CLAUDE_SONNET_4 = "anthropic/claude-sonnet-4-latest"
    ANTHROPIC_CLAUDE_OPUS_4 = "anthropic/claude-opus-4-latest"
    ANTHROPIC_CLAUDE_HAIKU_4 = "anthropic/claude-haiku-4-latest"
    ANTHROPIC_CLAUDE_3_7_SONNET = "anthropic/claude-3-7-sonnet-latest"
    ANTHROPIC_CLAUDE_3_5_SONNET = "anthropic/claude-3-5-sonnet-latest"
    ANTHROPIC_CLAUDE_3_5_HAIKU = "anthropic/claude-3-5-haiku-latest"

    # Google
    GEMINI_2_5_PRO = "google/gemini-2.5-pro"
    GEMINI_2_5_FLASH = "google/gemini-2.5-flash"
    GEMINI_2_0_FLASH = "google/gemini-2.0-flash"

    # Mistral
    MISTRAL_LARGE = "mistral/mistral-large-latest"
    MISTRAL_SMALL = "mistral/mistral-small-latest"
    PIXTRAL_LARGE = "mistral/pixtral-large-latest"
    MISTRAL_NEMO = "mistral/open-mistral-nemo"

    # Perplexity
    PERPLEXITY_SONAR = "perplexity/sonar"
    PERPLEXITY_SONAR_PRO = "perplexity/sonar-pro"
    PERPLEXITY_SONAR_REASONING = "perplexity/sonar-reasoning"

    # Groq (current — Aug 2026)
    # Note: model IDs after "groq/" are sent to Groq as-is (may include org prefix).
    GROQ_GPT_OSS_120B = "groq/openai/gpt-oss-120b"
    GROQ_GPT_OSS_20B = "groq/openai/gpt-oss-20b"
    GROQ_GPT_OSS_SAFEGUARD_20B = "groq/openai/gpt-oss-safeguard-20b"
    GROQ_QWEN3_6_27B = "groq/qwen/qwen3.6-27b"
    GROQ_COMPOUND = "groq/groq/compound"
    GROQ_COMPOUND_MINI = "groq/groq/compound-mini"

    # xAI
    XAI_GROK_3 = "xai/grok-3"
    XAI_GROK_2 = "xai/grok-2-1212"
    XAI_GROK_2_MINI = "xai/grok-2-mini-1212"

class TokenBee:
    def __init__(
        self, 
        api_key: str,
        llm_key: str,
        compression: str = "auto", 
        rate: str = CompressionRate.MEDIUM, 
        strategy: str = CompressionStrategy.SMART,
        context: str = TokenBeeContext.AUTO,
        model: str = "", 
        provider: str = "",
        privacy: bool = False
    ):
        self.api_key = api_key
        self.llm_key = llm_key
        self._base_url = "https://api.tokenbee.io/v1"
        self.headers = {
            "Authorization": f"Bearer {api_key}",
            "X-TokenBee-Key": api_key,
            "X-TB-Key": api_key,
            "X-LLM-Key": llm_key,
            "X-TokenBee-Compression": compression,
            "X-TokenBee-Rate": rate,
            "X-TokenBee-Strategy": strategy,
            "X-TokenBee-Context": context,
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
        if "strategy" in input:
            headers["X-TokenBee-Strategy"] = str(input["strategy"])
        if "context" in input:
            headers["X-TokenBee-Context"] = str(input["context"])
        if "privacy" in input:
            headers["X-TokenBee-Privacy"] = str(input["privacy"]).lower()
        if "sessionId" in input:
            headers["X-TB-Session-Id"] = str(input["sessionId"])
        if "userId" in input:
            headers["X-TB-User-Id"] = str(input["userId"])

        payload = input.copy()
        payload["model"] = model_name
        payload.pop("compression", None)
        payload.pop("rate", None)
        payload.pop("strategy", None)
        payload.pop("context", None)
        payload.pop("privacy", None)
        payload.pop("sessionId", None)
        payload.pop("userId", None)

        response = self.client.post("/chat/completions", json=payload, headers=headers)
        response.raise_for_status()
        return response.json()
