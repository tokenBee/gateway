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

EXPOSE 8001

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8001"]