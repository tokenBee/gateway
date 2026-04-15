FROM python:3.11-slim

WORKDIR /app

RUN apt-get update && apt-get install -y gcc g++ curl \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
# Install CPU-specific PyTorch to save ~4GB of space
RUN pip install --no-cache-dir torch --index-url https://download.pytorch.org/whl/cpu
RUN pip install --no-cache-dir -r requirements.txt

COPY main.py .

ENV HF_HOME=/app/model-cache

EXPOSE 8080
CMD ["sh", "-c", "gunicorn -w 1 -k uvicorn.workers.UvicornWorker main:app --bind 0.0.0.0:${PORT:-8080}"]
