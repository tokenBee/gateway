# TokenBee Python SDK

Official Python SDK for [TokenBee](https://tokenbee.io) — AI interaction capture, audit, replay, and optimization.

## Features

- **Unified API**: Access multiple LLM providers (OpenAI, Anthropic, Google, Mistral, etc.) through a single interface.
- **Intelligent Compression**: Reduce token usage and latency with context-aware compression.
- **Configurable Capture**: Retain interaction content when you need it; turn capture off per request or globally.
- **Built-in Observability**: Automatic tracking of latency, costs, and token usage.

## Links

- **Homepage**: [https://tokenbee.io](https://tokenbee.io)
- **Dashboard**: [https://tokenbee.io/dashboard](https://tokenbee.io/dashboard)

## Installation

```bash
pip install tokenbee-sdk
```

## Quick Start

```python
from tokenbee import TokenBee, TokenBeeModel, CompressionRate

# Initialize the client with your TokenBee API key 
# AND your LLM provider key (Bring Your Own Key - BYOK)
client = TokenBee(
    api_key="your_tokenbee_api_key",
    llm_key="your_llm_provider_key", # e.g. OpenAI or Anthropic key
    compression="auto",
    rate=CompressionRate.MEDIUM
)

# Send a request
response = client.send(
    model=TokenBeeModel.ANTHROPIC_CLAUDE_SONNET_4,
    input={
        "messages": [
            {"role": "user", "content": "Explain quantum entanglement in simple terms."}
        ]
    }
)

print(response["choices"][0]["message"]["content"])
```

## Bring Your Own Key (BYOK)

TokenBee is a **stateless** gateway. We do not store your LLM provider API keys in our database. You pass your provider key (OpenAI, Anthropic, etc.) through the SDK's `llm_key` parameter. The SDK sends this in the `X-LLM-Key` header, allowing the TokenBee proxy to forward requests to the provider on your behalf while you maintain full control over your billing and security.

## Advanced Usage

### Compression Control

You can specify the compression rate and method per request. TokenBee uses an intelligent semantic engine to reduce token usage while preserving meaning.

```python
response = client.send(
    model=TokenBeeModel.ANTHROPIC_CLAUDE_SONNET_4,
    input={
        "messages": [...],
        "compression": "auto",      # "auto" (default), "on", or "off"
        "rate": CompressionRate.HIGH, # MEDIUM (0.5), HIGH (0.33), etc.
        "capture": False            # per-request: do not retain message/response bodies
    }
)
```

- **`compression`**: Set to `"auto"` to let TokenBee decide when to compress, or `"off"` to bypass the compression engine entirely for high-precision tasks.
- **`rate`**: Controls the aggressiveness of compression. `HIGH` aims for ~67% token reduction.
- **`sessionId`**: (Optional) String ID to group multiple requests into a single replayable session in the dashboard.
- **`userId`**: (Optional) String ID to track usage and costs per unique end-user.
- **`capture`**: (Optional) Per-request override. `False` does not retain messages/responses (metadata like tokens/cost/latency can still be recorded). Precedence: per-request → project setting → default on.

### Supported Models

The SDK provides a `TokenBeeModel` enum with popular models:

- `TokenBeeModel.ANTHROPIC_CLAUDE_SONNET_4`
- `TokenBeeModel.OPENAI_GPT_5_MINI`
- `TokenBeeModel.GROQ_GPT_OSS_20B`
- ... and many others.

## License

MIT
