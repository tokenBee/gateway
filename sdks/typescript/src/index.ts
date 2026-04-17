export enum CompressionRate {
  Low = "0.75",
  Medium = "0.5",
  High = "0.33",
  Extreme = "0.2"
}

export enum TokenBeeModel {
  // OpenAI
  OpenAIGPT4o = "openai/gpt-4o",
  OpenAIGPT4oMini = "openai/gpt-4o-mini",
  OpenAIO1 = "openai/o1",
  OpenAIO1Mini = "openai/o1-mini",
  OpenAIO3Mini = "openai/o3-mini",

  // Anthropic
  AnthropicClaude4_6_Sonnet = "anthropic/claude-4-6-sonnet-latest",
  AnthropicClaude4_6_Opus = "anthropic/claude-4-6-opus-latest",
  AnthropicClaude3_5_Sonnet = "anthropic/claude-3-5-sonnet-latest",
  AnthropicClaude3_5_Haiku = "anthropic/claude-3-5-haiku-latest",
  AnthropicClaude3_Opus = "anthropic/claude-3-opus-latest",

  // Google
  Gemini3_1_Pro = "google/gemini-3.1-pro",
  Gemini3_1_Flash = "google/gemini-3.1-flash",
  Gemini2Flash = "google/gemini-2.0-flash",
  Gemini2Pro = "google/gemini-2.0-pro-exp",
  Gemini1_5_Pro = "google/gemini-1.5-pro",
  Gemini1_5_Flash = "google/gemini-1.5-flash",

  // Mistral
  MistralLarge = "mistral/mistral-large-latest",
  MistralSmall = "mistral/mistral-small-latest",
  PixtralLarge = "mistral/pixtral-large-latest",
  MistralNemo = "mistral/open-mistral-nemo",

  // Perplexity
  PerplexitySonar = "perplexity/sonar",
  PerplexitySonarPro = "perplexity/sonar-pro",
  PerplexitySonarReasoning = "perplexity/sonar-reasoning",

  // Groq
  GroqLlama3_3_70b = "groq/llama-3.3-70b-versatile",
  GroqLlama3_1_8b = "groq/llama-3.1-8b-instant",
  GroqMixtral8x7b = "groq/mixtral-8x7b-32768",
  GroqGemma2_9b = "groq/gemma2-9b-it",
}

export interface TokenBeeOptions {
  compression?: string;
  rate?: CompressionRate | string;
  model?: TokenBeeModel | string;
  provider?: string;
  privacy?: boolean;
  baseUrl?: string;
}

export class TokenBee {
  private apiKey: string;
  private llmKey: string;
  private options: TokenBeeOptions;

  constructor(config: { apiKey: string, llmKey: string, options?: TokenBeeOptions }) {
    this.apiKey = config.apiKey;
    this.llmKey = config.llmKey;
    this.options = config.options || {};
  }

  async send(params: { model: TokenBeeModel | string, input: any }) {
    const parts = params.model.split('/');
    const provider = parts.length > 1 ? parts[0] : (this.options.provider || "openai");
    const modelName = parts.length > 1 ? parts.slice(1).join('/') : parts[0];

    const baseUrl = this.options.baseUrl || "https://api.tokenbee.dev/v1";
    const headers: Record<string, string> = {
      "Authorization": `Bearer ${this.apiKey}`,
      "X-LLM-Key": this.llmKey,
      "Content-Type": "application/json",
      "X-TokenBee-Compression": params.input.compression || this.options.compression || "auto",
      "X-TokenBee-Rate": params.input.rate || this.options.rate || CompressionRate.Medium,
      "X-TokenBee-Model": modelName,
      "X-TokenBee-Provider": provider,
    };

    const privacy = params.input.privacy !== undefined ? params.input.privacy : this.options.privacy;
    if (privacy !== undefined) {
      headers["X-TokenBee-Privacy"] = String(privacy);
    }

    const payload = { ...params.input };
    // Remove SDK-specific keys from the payload sent to the LLM
    delete payload.compression;
    delete payload.rate;
    delete payload.privacy;

    const response = await fetch(`${baseUrl}/chat/completions`, {
      method: "POST",
      headers,
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      throw new Error(`TokenBee HTTP error: ${response.statusText}`);
    }
    return response.json();
  }
}
