"""UniSphere RAG adapter (lightweight).

Implements the HTTP contract the ASP.NET Core app expects (Services/RagClient.cs):

    GET  /health
    POST /retrieve  {query, documents[{id,title,text,metadata}], top_k}
                    -> {context, sources[{title,source,page,snippet,distance}], provider, embedding_model}
    POST /query     {question, documents[...], top_k, system_prompt}
                    -> {answer, sources[...], grounded, provider, model}

Documents are sent by the .NET app per request (already authorised for the signed-in
user), chunked and ranked in memory with BM25 - no vector DB, no model download, no
GPU. /retrieve needs NO LLM key. /query writes the answer with GEMINI_API_KEY when present,
else with a local Ollama (OLLAMA_BASE_URL, OLLAMA_MODEL), else falls back to an extractive
answer built from the best chunks, so the student "UniSphere RAG" provider never fails
just because a key is missing.

Run:  python -m uvicorn api:app --host 127.0.0.1 --port 8000
Env (optional, read from ./.env, ../.env or the process environment):
    RAG_API_KEY    if set, requests must send header X-RAG-Api-Key (match RagApi:ApiKey in .NET)
    GEMINI_API_KEY used by /query to write the answer
    GEMINI_MODEL   default gemini-flash-lite-latest
"""
from __future__ import annotations

import json
import math
import os
import re
import secrets
import urllib.error
import urllib.request
from collections import Counter
from pathlib import Path
from typing import Any, Dict, List, Optional

from fastapi import Depends, FastAPI, Header, HTTPException
from pydantic import BaseModel, Field


def _load_env() -> None:
    here = Path(__file__).resolve().parent
    for candidate in (here / ".env", here.parent / ".env"):
        if not candidate.is_file():
            continue
        for raw in candidate.read_text(encoding="utf-8-sig").splitlines():
            line = raw.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            name, value = line.split("=", 1)
            name, value = name.strip(), value.strip().strip('"').strip("'")
            if name and value and not os.environ.get(name):
                os.environ[name] = value


_load_env()
RAG_API_KEY = os.getenv("RAG_API_KEY", "")
GEMINI_MODEL = os.getenv("GEMINI_MODEL", "gemini-flash-lite-latest")

app = FastAPI(title="UniSphere RAG adapter", version="1.0")


def verify_key(x_rag_api_key: Optional[str] = Header(default=None, alias="X-RAG-Api-Key")) -> None:
    if RAG_API_KEY and not secrets.compare_digest(x_rag_api_key or "", RAG_API_KEY):
        raise HTTPException(status_code=401, detail="Invalid API key")


class Doc(BaseModel):
    id: Optional[str] = None
    title: str = ""
    text: str = ""
    metadata: Optional[Dict[str, str]] = None


class RetrieveRequest(BaseModel):
    query: str = Field(..., min_length=1)
    documents: List[Doc] = Field(default_factory=list)
    top_k: int = Field(default=4, ge=1, le=8)


class QueryRequest(BaseModel):
    question: str = Field(..., min_length=1)
    documents: List[Doc] = Field(default_factory=list)
    top_k: int = Field(default=4, ge=1, le=8)
    system_prompt: Optional[str] = None


# ----------------------------------------------------------------------------
# Chunking + BM25
# ----------------------------------------------------------------------------
_TOKEN = re.compile(r"[\w؀-ۿ]+", re.UNICODE)
_STOP = {"the", "and", "for", "are", "how", "what", "why", "when", "with", "from", "that",
         "this", "please", "explain", "about", "does", "can", "you", "ما", "هل", "كيف", "في",
         "من", "على", "عن", "هو", "هي"}


def tokens(text: str) -> List[str]:
    return [t for t in (m.lower() for m in _TOKEN.findall(text)) if len(t) > 1 and t not in _STOP]


def chunk(text: str, size: int = 800, overlap: int = 100) -> List[str]:
    text = re.sub(r"\s+", " ", text).strip()
    if len(text) <= size:
        return [text] if text else []
    out, i = [], 0
    while i < len(text):
        end = min(len(text), i + size)
        if end < len(text):  # prefer to cut at a sentence/space boundary
            cut = max(text.rfind(". ", i, end), text.rfind(" ", i + size // 2, end))
            if cut > i:
                end = cut + 1
        out.append(text[i:end].strip())
        if end >= len(text):
            break
        i = max(end - overlap, i + 1)
    return [c for c in out if c]


SMALL_DOCS_CHARS = 9000


def rank(query: str, docs: List[Doc], top_k: int) -> List[Dict[str, Any]]:
    chunks: List[Dict[str, Any]] = []
    for d in docs:
        for n, c in enumerate(chunk(d.text)):
            chunks.append({"title": d.title, "source": d.id, "text": c, "n": n, "tok": tokens(d.title + " " + c)})
    q = tokens(query)
    if not chunks or not q:
        return []
    N = len(chunks)
    avg = sum(len(c["tok"]) for c in chunks) / N or 1.0
    df = Counter(t for c in chunks for t in set(c["tok"]))
    k1, b = 1.5, 0.75
    for c in chunks:
        tf = Counter(c["tok"])
        score = 0.0
        for t in set(q):
            if t not in tf:
                continue
            idf = math.log(1 + (N - df[t] + 0.5) / (df[t] + 0.5))
            score += idf * tf[t] * (k1 + 1) / (tf[t] + k1 * (1 - b + b * len(c["tok"]) / avg))
        c["score"] = score
    # Small document sets (a single CV, a short handout) fit whole: keep every chunk
    # in reading order so nothing is dropped by top_k.
    if sum(len(c["text"]) for c in chunks) <= SMALL_DOCS_CHARS:
        return chunks
    best = sorted((c for c in chunks if c["score"] > 0), key=lambda c: -c["score"])[:top_k]
    return best


def to_sources(best: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    return [{"title": c["title"], "source": c["source"], "page": None,
             "snippet": c["text"][:240], "distance": round(1.0 / (1.0 + c["score"]), 4)} for c in best]


def to_context(best: List[Dict[str, Any]]) -> str:
    return "\n\n".join(f"[{c['title']}]\n{c['text']}" for c in best)


# ----------------------------------------------------------------------------
# Routes
# ----------------------------------------------------------------------------
@app.get("/health")
def health() -> Dict[str, str]:
    return {"status": "ok"}


@app.post("/retrieve", dependencies=[Depends(verify_key)])
def retrieve(req: RetrieveRequest) -> Dict[str, Any]:
    best = rank(req.query, req.documents, req.top_k)
    return {"context": to_context(best), "sources": to_sources(best),
            "provider": "bm25", "embedding_model": "bm25"}


def _gemini(system: str, prompt: str) -> Optional[str]:
    key = os.getenv("GEMINI_API_KEY", "")
    if not key:
        return None
    body = {"systemInstruction": {"parts": [{"text": system}]},
            "contents": [{"role": "user", "parts": [{"text": prompt}]}],
            "generationConfig": {"maxOutputTokens": 3072}}
    req = urllib.request.Request(
        f"https://generativelanguage.googleapis.com/v1beta/models/{GEMINI_MODEL}:generateContent",
        data=json.dumps(body).encode(), method="POST",
        headers={"Content-Type": "application/json", "x-goog-api-key": key})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            data = json.load(r)
        parts = data["candidates"][0]["content"]["parts"]
        return "".join(p.get("text", "") for p in parts if not p.get("thought")).strip() or None
    except (urllib.error.URLError, KeyError, IndexError, ValueError, TimeoutError):
        return None


def _ollama(system: str, prompt: str) -> Optional[str]:
    """Local fallback writer: a running Ollama (OLLAMA_BASE_URL / OLLAMA_MODEL)."""
    base = os.getenv("OLLAMA_BASE_URL", "http://127.0.0.1:11434").rstrip("/").removesuffix("/v1")
    # keep_alive: reloading the model after Ollama's default 5 idle minutes takes 30s+ on this CPU.
    # num_ctx: Ollama's default window (2-4k tokens) silently cuts long prompts (a CV + instructions).
    body = {"model": os.getenv("OLLAMA_MODEL", "qwen3:1.7b"), "stream": False, "think": False, "keep_alive": "30m",
            "options": {"num_ctx": 8192},
            "messages": [{"role": "system", "content": system}, {"role": "user", "content": prompt}]}
    req = urllib.request.Request(f"{base}/api/chat", data=json.dumps(body).encode(), method="POST",
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            text = json.load(r)["message"]["content"]
        # qwen3 may still wrap reasoning in <think> tags: keep only the answer.
        return re.sub(r"<think>.*?</think>", "", text, flags=re.S).strip() or None
    except (urllib.error.URLError, KeyError, ValueError, TimeoutError, OSError):
        return None


def _write(system: str, prompt: str) -> tuple[Optional[str], str, str]:
    """Answer with Gemini when a key is set, else a local Ollama; (answer, provider, model)."""
    answer = _gemini(system, prompt)
    if answer:
        return answer, "gemini", GEMINI_MODEL
    answer = _ollama(system, prompt)
    return answer, "ollama", os.getenv("OLLAMA_MODEL", "qwen3:1.7b")


@app.post("/query", dependencies=[Depends(verify_key)])
def query(req: QueryRequest) -> Dict[str, Any]:
    best = rank(req.question, req.documents, req.top_k)
    sources = to_sources(best)
    system = (req.system_prompt or "You are a university teaching assistant.") + \
        " Use the provided course excerpts first when relevant and cite the excerpt title. If they do not cover the" \
        " question, do NOT refuse: answer helpfully from general knowledge and say briefly that it came from general" \
        " knowledge rather than the course materials."
    if not best:
        answer, provider, model = _write(system, f"Course excerpts: (none matched)\n\nQuestion: {req.question}")
        if answer:
            return {"answer": answer, "sources": [], "grounded": False, "provider": provider, "model": model}
        return {"answer": "I could not find this in the provided course materials, and no language model is available for a general answer right now.",
                "sources": [], "grounded": False, "provider": "bm25", "model": "none"}
    answer, provider, model = _write(system, f"Course excerpts:\n{to_context(best)}\n\nQuestion: {req.question}")
    if answer:
        return {"answer": answer, "sources": sources, "grounded": True, "provider": provider, "model": model}
    extract = "\n\n".join(f"From \"{c['title']}\": {c['text']}" for c in best[:3])
    return {"answer": "Here are the most relevant passages from your course materials:\n\n" + extract,
            "sources": sources, "grounded": True, "provider": "extractive", "model": "bm25"}
