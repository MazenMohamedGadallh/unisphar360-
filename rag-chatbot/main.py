import os
import secrets
import logging
from contextlib import asynccontextmanager
from typing import Optional, List
from pathlib import Path

from fastapi import FastAPI, HTTPException, Depends, Header, UploadFile, File, Form, status
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field, validator
from dotenv import load_dotenv

from langchain_community.document_loaders import PDFPlumberLoader
from langchain_text_splitters import RecursiveCharacterTextSplitter
from langchain_community.embeddings import HuggingFaceEmbeddings
from langchain_community.vectorstores import Chroma
from langchain_groq import ChatGroq
from langchain_core.prompts import ChatPromptTemplate
from langchain_core.runnables import RunnablePassthrough
from langchain_core.output_parsers import StrOutputParser
from langchain_core.documents import Document

load_dotenv()

GROQ_API_KEY = os.getenv("GROQ_API_KEY")
RAG_API_KEY = os.getenv("RAG_API_KEY")
GROQ_MODEL = os.getenv("GROQ_MODEL", "llama-3.1-8b-instant")
CHROMA_PERSIST_DIR = "./chroma_db"
COLLECTION_NAME = "rag_documents"
MAX_PDF_SIZE = 20 * 1024 * 1024  # 20 MB

if not GROQ_API_KEY:
    raise RuntimeError("GROQ_API_KEY environment variable is required")
if not RAG_API_KEY:
    raise RuntimeError("RAG_API_KEY environment variable is required")

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
)
logger = logging.getLogger(__name__)

embeddings = HuggingFaceEmbeddings(
    model_name="sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
)

text_splitter = RecursiveCharacterTextSplitter(
    chunk_size=800,
    chunk_overlap=100
)

vectorstore = None


def get_vectorstore() -> Chroma:
    global vectorstore
    if vectorstore is None:
        vectorstore = Chroma(
            persist_directory=CHROMA_PERSIST_DIR,
            embedding_function=embeddings,
            collection_name=COLLECTION_NAME
        )
    return vectorstore


def verify_api_key(x_api_key: str = Header(..., alias="X-API-Key")):
    if not secrets.compare_digest(x_api_key, RAG_API_KEY):
        logger.warning("Invalid API key attempt")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid API key"
        )
    return x_api_key


class IngestRequest(BaseModel):
    university_id: int = Field(..., ge=1)
    course_code: str = Field(..., min_length=1, max_length=50)
    material_id: str = Field(..., min_length=1, max_length=100)
    title: str = Field(..., min_length=1, max_length=200)
    text: str = Field(..., min_length=1)


class QueryFilters(BaseModel):
    university_id: int = Field(..., ge=1)
    course_codes: Optional[List[str]] = Field(default=None)


class QueryRequest(BaseModel):
    question: str = Field(..., min_length=1, max_length=2000)
    top_k: int = Field(default=5, ge=1, le=20)
    filters: QueryFilters


class ChunkResponse(BaseModel):
    text: str
    course_code: str
    title: str
    score: float


class QueryResponse(BaseModel):
    chunks: List[ChunkResponse]


class AnswerRequest(BaseModel):
    question: str = Field(..., min_length=1, max_length=2000)
    top_k: int = Field(default=5, ge=1, le=20)
    filters: QueryFilters


class AnswerResponse(BaseModel):
    answer: str
    sources: List[ChunkResponse]


class HealthResponse(BaseModel):
    status: str


def build_metadata_filter(university_id: int, course_codes: Optional[List[str]] = None) -> dict:
    if course_codes and len(course_codes) > 0:
        return {
            "$and": [
                {"university_id": {"$eq": university_id}},
                {"course_code": {"$in": course_codes}}
            ]
        }
    return {"university_id": {"$eq": university_id}}


def format_docs(docs):
    return "\n\n".join(doc.page_content for doc in docs if doc.page_content)


def get_llm():
    return ChatGroq(
        model_name=GROQ_MODEL,
        temperature=0.0,
        api_key=GROQ_API_KEY
    )


SYSTEM_PROMPT = (
    "You are a concise, factual assistant.\n"
    "Answer the question strictly based ONLY on the context below.\n"
    "Do NOT repeat yourself. Be direct and brief.\n"
    "Always answer in the same language as the user's question.\n"
    "If the answer is NOT in the context, reply with ONLY ONE single sentence: "
    "\"عذراً، هذه المعلومة غير موجودة في المستند المرفق.\"\n\n"
    "Context:\n{context}"
)


@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("Starting RAG API service")
    get_vectorstore()
    yield
    logger.info("Shutting down RAG API service")


app = FastAPI(
    title="RAG Knowledge Base API",
    version="1.0.0",
    lifespan=lifespan
)


@app.exception_handler(Exception)
async def global_exception_handler(request, exc):
    logger.error(f"Unhandled error: {type(exc).__name__}")
    return JSONResponse(
        status_code=500,
        content={"detail": "Internal server error"}
    )


@app.get("/health", response_model=HealthResponse)
async def health():
    return {"status": "ok"}


@app.post("/ingest", dependencies=[Depends(verify_api_key)])
async def ingest_text(request: IngestRequest):
    vs = get_vectorstore()
    
    existing = vs.get(where={"material_id": {"$eq": request.material_id}})
    if existing and existing.get("ids"):
        vs.delete(ids=existing["ids"])
        logger.info(f"Deleted existing chunks for material_id: {request.material_id}")
    
    doc = Document(
        page_content=request.text,
        metadata={
            "university_id": request.university_id,
            "course_code": request.course_code,
            "material_id": request.material_id,
            "title": request.title
        }
    )
    
    chunks = text_splitter.split_documents([doc])
    
    for chunk in chunks:
        chunk.metadata.update({
            "university_id": request.university_id,
            "course_code": request.course_code,
            "material_id": request.material_id,
            "title": request.title
        })
    
    vs.add_documents(chunks)
    vs.persist()
    
    logger.info(f"Ingested {len(chunks)} chunks for material_id: {request.material_id}")
    return {"status": "success", "chunks_created": len(chunks)}


@app.post("/ingest/pdf", dependencies=[Depends(verify_api_key)])
async def ingest_pdf(
    university_id: int = Form(..., ge=1),
    course_code: str = Form(..., min_length=1, max_length=50),
    material_id: str = Form(..., min_length=1, max_length=100),
    title: str = Form(..., min_length=1, max_length=200),
    file: UploadFile = File(...)
):
    if not file.filename or not file.filename.lower().endswith(".pdf"):
        raise HTTPException(status_code=400, detail="File must be a PDF")
    
    content = await file.read()
    if len(content) > MAX_PDF_SIZE:
        raise HTTPException(status_code=413, detail="File size exceeds 20 MB limit")
    
    if len(content) == 0:
        raise HTTPException(status_code=400, detail="Empty file")
    
    import tempfile
    with tempfile.NamedTemporaryFile(delete=False, suffix=".pdf") as tmp_file:
        tmp_file.write(content)
        tmp_path = tmp_file.name
    
    try:
        loader = PDFPlumberLoader(tmp_path)
        docs = loader.load()
        
        vs = get_vectorstore()
        
        existing = vs.get(where={"material_id": {"$eq": material_id}})
        if existing and existing.get("ids"):
            vs.delete(ids=existing["ids"])
            logger.info(f"Deleted existing chunks for material_id: {material_id}")
        
        for doc in docs:
            doc.metadata.update({
                "university_id": university_id,
                "course_code": course_code,
                "material_id": material_id,
                "title": title
            })
        
        chunks = text_splitter.split_documents(docs)
        
        for chunk in chunks:
            chunk.metadata.update({
                "university_id": university_id,
                "course_code": course_code,
                "material_id": material_id,
                "title": title
            })
        
        vs.add_documents(chunks)
        vs.persist()
        
        logger.info(f"Ingested PDF {len(chunks)} chunks for material_id: {material_id}")
        return {"status": "success", "chunks_created": len(chunks)}
    finally:
        if os.path.exists(tmp_path):
            os.remove(tmp_path)


@app.delete("/materials/{material_id}", dependencies=[Depends(verify_api_key)])
async def delete_material(material_id: str):
    vs = get_vectorstore()
    
    existing = vs.get(where={"material_id": {"$eq": material_id}})
    if not existing or not existing.get("ids"):
        raise HTTPException(status_code=404, detail="Material not found")
    
    vs.delete(ids=existing["ids"])
    vs.persist()
    
    logger.info(f"Deleted material_id: {material_id} ({len(existing['ids'])} chunks)")
    return {"status": "success", "chunks_deleted": len(existing["ids"])}


@app.post("/query", response_model=QueryResponse, dependencies=[Depends(verify_api_key)])
async def query(request: QueryRequest):
    vs = get_vectorstore()
    
    metadata_filter = build_metadata_filter(request.filters.university_id, request.filters.course_codes)
    
    results = vs.similarity_search_with_score(
        request.question,
        k=request.top_k,
        filter=metadata_filter
    )
    
    chunks = [
        ChunkResponse(
            text=doc.page_content,
            course_code=doc.metadata.get("course_code", ""),
            title=doc.metadata.get("title", ""),
            score=float(score)
        )
        for doc, score in results
    ]
    
    logger.info(f"Query returned {len(chunks)} chunks for university_id={request.filters.university_id}")
    return QueryResponse(chunks=chunks)


@app.post("/answer", response_model=AnswerResponse, dependencies=[Depends(verify_api_key)])
async def answer(request: AnswerRequest):
    vs = get_vectorstore()
    
    metadata_filter = build_metadata_filter(request.filters.university_id, request.filters.course_codes)
    
    retriever = vs.as_retriever(
        search_kwargs={"k": request.top_k, "filter": metadata_filter}
    )
    
    llm = get_llm()
    prompt = ChatPromptTemplate.from_messages([
        ("system", SYSTEM_PROMPT),
        ("human", "{question}")
    ])
    
    chain = (
        {"context": retriever | format_docs, "question": RunnablePassthrough()}
        | prompt
        | llm
        | StrOutputParser()
    )
    
    answer_text = chain.invoke(request.question)
    
    results = vs.similarity_search_with_score(
        request.question,
        k=request.top_k,
        filter=metadata_filter
    )
    
    sources = [
        ChunkResponse(
            text=doc.page_content,
            course_code=doc.metadata.get("course_code", ""),
            title=doc.metadata.get("title", ""),
            score=float(score)
        )
        for doc, score in results
    ]
    
    logger.info(f"Answer generated for university_id={request.filters.university_id}")
    return AnswerResponse(answer=answer_text, sources=sources)


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)