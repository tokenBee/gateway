# TokenBee Python SDK

Official Python SDK for [TokenBee](https://tokenbee.dev) - The Intelligent LLM Inference Gateway with Observability, Compression, and Privacy.

## Features

- **Unified API**: Access multiple LLM providers (OpenAI, Anthropic, Google, Mistral, etc.) through a single interface.
- **Intelligent Compression**: Reduce token usage and latency with context-aware compression.
- **Privacy Guard**: Automatic PII masking and privacy-preserving inference.
- **Built-in Observability**: Automatic tracking of latency, costs, and token usage.

## Installation

```bash
pip install tokenbee-sdk
```

## Quick Start

```python
from tokenbee import TokenBee, TokenBeeModel, CompressionRate

# Initialize the client
client = TokenBee(
    api_key="your_api_key_here",
    compression="auto",
    rate=CompressionRate.MEDIUM
)

# Send a request
response = client.send(
    model=TokenBeeModel.OPENAI_GPT_4O,
    input={
        "messages": [
            {"role": "user", "content": "Explain quantum entanglement in simple terms."}
        ]
    }
)

print(response["choices"][0]["message"]["content"])
```

## Advanced Usage

### Compression Control

You can specify the compression rate and method per request:

```python
response = client.send(
    model=TokenBeeModel.ANTHROPIC_CLAUDE_3_5_SONNET,
    input={
        "messages": [...],
        "compression": "on",
        "rate": CompressionRate.HIGH,
        "privacy": True
    }
)
```

### Supported Models

The SDK provides a `TokenBeeModel` enum with popular models:

- `TokenBeeModel.OPENAI_GPT_4O`
- `TokenBeeModel.ANTHROPIC_CLAUDE_3_5_SONNET`
- `TokenBeeModel.GEMINI_2_0_FLASH`
- ... and many others.

## License

MIT
