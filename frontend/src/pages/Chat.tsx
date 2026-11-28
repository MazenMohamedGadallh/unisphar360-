import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Markdown } from "../markdown";
import { copyToClipboard } from "../clipboard";
import { UiIcon } from "../UiIcon";
import { BrandLogo } from "../components/BrandLogo";
import { UserAvatar } from "../components/UserAvatar";
import { API_BASE_URL, authHeaders, getToken, type AuthUser } from "../lib/api";
import "../chat.css";

const PROVIDER_KEY = "unisphere.chat.provider";
const FALLBACK_ASSISTANT_ERROR = "The assistant couldn't complete the reply. Please try again.";

interface ChatProvider {
  name: string;
  displayName: string;
  model: string;
}

interface ChatProviderStatus extends ChatProvider {
  available: boolean;
  reason: string;
}

interface ChatSourceCitation {
  title: string;
  courseCode?: string;
  kind: "material" | "database" | "general";
}

interface ChatConversation {
  id: number;
  title: string;
  provider: string;
  updatedAtUtc: string;
  messageCount: number;
}

interface ChatMessage {
  id: number;
  role: string;
  content: string;
  provider: string;
  model: string;
  createdAtUtc: string;
  sources?: ChatSourceCitation[];
}

interface ChatConversationDetail {
  id: number;
  title: string;
  provider: string;
  updatedAtUtc: string;
  messages: ChatMessage[];
}

const ROLE_PROMPTS: Record<string, string[]> = {
  Student: [
    "Summarize my CS350 Machine Learning lecture notes",
    "Quiz me with 3 practice questions on SQL Joins & Indexing",
    "How can I improve my Computer Networks grade and attendance?",
    "Explain Dijkstra's shortest-path algorithm with a clean example",
  ],
  Doctor: [
    "Draft a grading rubric for a Machine Learning midterm essay",
    "Summarize key exam topics from CS330 Database Systems notes",
    "Suggest 4 challenging definition questions on Computer Networks",
    "Write an announcement reminding students about Hall B labs",
  ],
  Graduate: [
    "Tailor my CV summary for a Junior Full-Stack Engineer role",
    "Run a mock behavioral interview question using the STAR method",
    "What skills should I learn first to close my Kubernetes gap?",
    "Compare REST vs GraphQL for a system design interview",
  ],
  Admin: [
    "Summarize best practices for 4-year university curriculum prerequisites",
    "Draft a campus-wide announcement for Fall 2026 final exams",
    "Recommend security checklist items for university audit logs",
  ],
};

function extractErrorMessage(payload: string): string {
  try {
    const doc = JSON.parse(payload) as Record<string, unknown> | string;
    if (typeof doc === "string") return doc || FALLBACK_ASSISTANT_ERROR;
    if (doc && typeof doc === "object") {
      if (typeof doc.error === "string") return doc.error;
      if (typeof doc.message === "string") return doc.message;
    }
  } catch {
    // ignore
  }
  return FALLBACK_ASSISTANT_ERROR;
}

function localTime(isoUtc: string): string {
  return new Date(isoUtc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", hour12: false });
}

export function Chat() {
  const navigate = useNavigate();
  const [user, setUser] = useState<AuthUser | null>(null);
  const [authChecked, setAuthChecked] = useState(false);

  const [providers, setProviders] = useState<ChatProvider[]>([]);
  const [provider, setProvider] = useState("");
  const [providersReason, setProvidersReason] = useState("");

  const [conversations, setConversations] = useState<ChatConversation[] | null>(null);
  const [activeId, setActiveId] = useState<number | null>(null);
  const [messages, setMessages] = useState<ChatMessage[] | null>(null);

  const [input, setInput] = useState("");
  const [streaming, setStreaming] = useState(false);
  const [streamingText, setStreamingText] = useState("");
  const [streamingSources, setStreamingSources] = useState<ChatSourceCitation[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [copiedIdx, setCopiedIdx] = useState<number | null>(null);

  const [confirmDeleteId, setConfirmDeleteId] = useState<number | null>(null);
  const [renameId, setRenameId] = useState<number | null>(null);
  const [renameText, setRenameText] = useState("");

  const messagesRef = useRef<ChatMessage[] | null>(null);
  const activeIdRef = useRef<number | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const messagesElRef = useRef<HTMLDivElement>(null);

  function applyMessages(list: ChatMessage[] | null) {
    messagesRef.current = list;
    setMessages(list);
  }

  function applyActiveId(id: number | null) {
    activeIdRef.current = id;
    setActiveId(id);
  }

  const activeProviderModel = providers.find((p) => p.name === provider)?.model ?? "";

  useEffect(() => {
    document.title = "UniSphere 360+ — AI Assistant";
    let cancelled = false;
    void (async () => {
      if (!getToken()) {
        navigate("/login", { replace: true });
        return;
      }
      try {
        const res = await fetch(`${API_BASE_URL}/api/auth/me`, { headers: authHeaders() });
        if (!res.ok) {
          navigate("/login?expired=1", { replace: true });
          return;
        }
        const me = (await res.json()) as AuthUser;
        if (cancelled) return;
        setUser(me);
        setAuthChecked(true);
        await Promise.all([loadProviders(), loadConversations(true)]);
      } catch {
        if (!cancelled) setAuthChecked(true);
      }
    })();
    return () => {
      cancelled = true;
      abortRef.current?.abort();
    };
  }, [navigate]);

  async function loadProviders() {
    try {
      const res = await fetch(`${API_BASE_URL}/api/chat/providers`, { headers: authHeaders() });
      const list = res.ok ? ((await res.json()) as ChatProvider[]) : [];
      setProviders(list);
      if (list.length > 0) {
        const saved = localStorage.getItem(PROVIDER_KEY);
        const match = list.find((p) => p.name === saved)?.name ?? list[0].name;
        setProvider(match);
        setProvidersReason("");
      } else {
        const stRes = await fetch(`${API_BASE_URL}/api/chat/providers/status`, { headers: authHeaders() });
        const st = stRes.ok ? ((await stRes.json()) as ChatProviderStatus[]) : [];
        setProvidersReason(st.find((s) => s.reason)?.reason ?? "No AI provider is available right now.");
      }
    } catch {
      setProvidersReason("Could not check AI provider status.");
    }
  }

  async function loadConversations(selectMostRecent: boolean) {
    try {
      const res = await fetch(`${API_BASE_URL}/api/chat/conversations`, { headers: authHeaders() });
      const list = res.ok ? ((await res.json()) as ChatConversation[]) : [];
      setConversations(list);
      if (selectMostRecent && activeIdRef.current === null && list.length > 0) {
        await selectConversation(list[0].id);
      } else if (selectMostRecent && list.length === 0) {
        applyMessages([]);
      }
    } catch {
      setConversations([]);
      applyMessages([]);
    }
  }

  async function selectConversation(id: number) {
    applyActiveId(id);
    applyMessages(null);
    setError(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/chat/conversations/${id}`, { headers: authHeaders() });
      if (!res.ok) throw new Error();
      const detail = (await res.json()) as ChatConversationDetail;
      applyMessages(detail.messages ?? []);
    } catch {
      setError("Could not load this conversation.");
      applyMessages([]);
    }
  }

  function newChat() {
    applyActiveId(null);
    applyMessages([]);
    setError(null);
    setInput("");
  }

  function scrollToBottom() {
    requestAnimationFrame(() => {
      const el = messagesElRef.current;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }

  async function send(regenerate: boolean, overrideText?: string) {
    const text = regenerate
      ? (messagesRef.current?.filter((m) => m.role === "user").pop()?.content ?? "").trim()
      : (overrideText ?? input).trim();
    if (!text || streaming || !provider) return;

    setError(null);
    if (!regenerate) setInput("");
    setStreaming(true);
    setStreamingText("");
    setStreamingSources([]);

    const controller = new AbortController();
    abortRef.current = controller;
    let accumulated = "";
    let latestSources: ChatSourceCitation[] = [];

    try {
      let list = messagesRef.current ?? [];
      if (regenerate && list.length > 0 && list[list.length - 1].role === "assistant") {
        list = list.slice(0, -1);
        applyMessages(list);
      }
      if (!regenerate || !list.some((m) => m.role === "user")) {
        list = [
          ...list,
          {
            id: Date.now(),
            role: "user",
            content: text,
            provider,
            model: activeProviderModel,
            createdAtUtc: new Date().toISOString(),
          },
        ];
        applyMessages(list);
      }
      scrollToBottom();

      const convId = activeIdRef.current?.toString() ?? "new";
      const url = `${API_BASE_URL}/api/chat/conversations/${convId}/messages${regenerate ? "?regenerate=true" : ""}`;
      const response = await fetch(url, {
        method: "POST",
        headers: { ...authHeaders(), "Content-Type": "application/json" },
        body: JSON.stringify({ provider, content: text }),
        signal: controller.signal,
      });

      if (!response.ok) {
        const raw = await response.text();
        setError(extractErrorMessage(raw));
        return;
      }

      const convHeader = response.headers.get("X-Conversation-Id");
      if (convHeader && !Number.isNaN(Number(convHeader))) {
        applyActiveId(Number(convHeader));
        void loadConversations(false);
      }

      if (!response.body) throw new Error("No response stream.");
      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";
      let currentEvent: string | null = null;
      let done = false;

      while (!done) {
        const { done: streamDone, value } = await reader.read();
        if (streamDone) break;
        buffer += decoder.decode(value, { stream: true });
        let nl = buffer.indexOf("\n");
        while (nl >= 0) {
          const line = buffer.slice(0, nl).replace(/\r$/, "");
          buffer = buffer.slice(nl + 1);
          nl = buffer.indexOf("\n");
          if (line === "") {
            currentEvent = null;
            continue;
          }
          if (line.startsWith("event:")) {
            currentEvent = line.slice(6).trim();
            continue;
          }
          if (!line.startsWith("data:")) continue;
          const payload = line.slice(5).trim();
          if (payload === "[DONE]") {
            done = true;
            break;
          }
          if (currentEvent === "sources") {
            try {
              const parsed = JSON.parse(payload) as ChatSourceCitation[];
              if (Array.isArray(parsed)) {
                latestSources = parsed;
                setStreamingSources(parsed);
              }
            } catch {
              // ignore
            }
            continue;
          }
          if (currentEvent === "error") {
            setError(extractErrorMessage(payload));
            done = true;
            break;
          }
          try {
            const token = JSON.parse(payload) as unknown;
            if (typeof token === "string" && token.length > 0) {
              accumulated += token;
              setStreamingText(accumulated);
              scrollToBottom();
            }
          } catch {
            // ignore
          }
        }
      }

      if (accumulated.length > 0) {
        applyMessages([
          ...(messagesRef.current ?? []),
          {
            id: Date.now() + 1,
            role: "assistant",
            content: accumulated,
            provider,
            model: activeProviderModel,
            createdAtUtc: new Date().toISOString(),
            sources: latestSources,
          },
        ]);
        void loadConversations(false);
      }
    } catch (err) {
      const aborted = err instanceof DOMException && err.name === "AbortError";
      if (aborted && accumulated.length > 0) {
        applyMessages([
          ...(messagesRef.current ?? []),
          {
            id: Date.now() + 1,
            role: "assistant",
            content: accumulated,
            provider,
            model: activeProviderModel,
            createdAtUtc: new Date().toISOString(),
            sources: latestSources,
          },
        ]);
      } else if (!aborted) {
        setError(FALLBACK_ASSISTANT_ERROR);
      }
    } finally {
      setStreaming(false);
      setStreamingText("");
      setStreamingSources([]);
      abortRef.current = null;
      scrollToBottom();
    }
  }

  async function renameConversation(id: number) {
    if (!renameText.trim()) return setRenameId(null);
    await fetch(`${API_BASE_URL}/api/chat/conversations/${id}`, {
      method: "PATCH",
      headers: { ...authHeaders(), "Content-Type": "application/json" },
      body: JSON.stringify({ title: renameText.trim() }),
    });
    setRenameId(null);
    void loadConversations(false);
  }

  async function deleteConfirmed() {
    if (confirmDeleteId === null) return;
    await fetch(`${API_BASE_URL}/api/chat/conversations/${confirmDeleteId}`, {
      method: "DELETE",
      headers: authHeaders(),
    });
    if (activeIdRef.current === confirmDeleteId) newChat();
    setConfirmDeleteId(null);
    void loadConversations(false);
  }

  function handleKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      void send(false);
    }
  }

  if (!authChecked) return null;

  const quickPrompts = ROLE_PROMPTS[user?.role ?? "Student"] ?? ROLE_PROMPTS.Student;

  return (
    <div className="us-chat-shell">
      {/* Left Conversation History Rail */}
      <aside className="us-chat-sidebar">
        <div className="flex items-center justify-between gap-2 border-b border-border p-3.5">
          <button
            type="button"
            onClick={newChat}
            className="btn-luminous flex flex-1 cursor-pointer items-center justify-center gap-2 rounded-control px-3.5 py-2.5 text-xs font-semibold"
          >
            <UiIcon name="plus" /> New Chat
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-2.5">
          {conversations === null ? (
            <div className="p-4 text-center text-xs text-text-muted">Loading history…</div>
          ) : conversations.length === 0 ? (
            <div className="p-4 text-center text-xs text-text-muted">No conversations yet.</div>
          ) : (
            conversations.map((c) => (
              <div
                key={c.id}
                className={`group mb-1 flex items-center gap-2 rounded-control px-3 py-2.5 text-xs transition-colors ${
                  activeId === c.id
                    ? "bg-primary-light font-semibold text-primary"
                    : "text-text-secondary hover:bg-primary-light/50 hover:text-text"
                }`}
              >
                {renameId === c.id ? (
                  <input
                    value={renameText}
                    onChange={(e) => setRenameText(e.target.value)}
                    onBlur={() => void renameConversation(c.id)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") void renameConversation(c.id);
                      if (e.key === "Escape") setRenameId(null);
                    }}
                    autoFocus
                    className="min-w-0 flex-1 rounded border border-primary bg-surface px-2 py-1 text-xs text-text outline-none"
                  />
                ) : (
                  <button
                    type="button"
                    onClick={() => void selectConversation(c.id)}
                    className="min-w-0 flex-1 cursor-pointer truncate border-0 bg-transparent text-start text-inherit"
                  >
                    {c.title}
                  </button>
                )}
                <div className="flex shrink-0 items-center gap-1 opacity-0 transition-opacity group-hover:opacity-100">
                  <button
                    type="button"
                    onClick={() => {
                      setRenameId(c.id);
                      setRenameText(c.title);
                    }}
                    title="Rename"
                    className="cursor-pointer rounded border-0 bg-transparent p-1 text-text-muted hover:text-primary"
                  >
                    <UiIcon name="pencil" />
                  </button>
                  <button
                    type="button"
                    onClick={() => setConfirmDeleteId(c.id)}
                    title="Delete"
                    className="cursor-pointer rounded border-0 bg-transparent p-1 text-text-muted hover:text-danger"
                  >
                    <UiIcon name="trash-2" />
                  </button>
                </div>
              </div>
            ))
          )}
        </div>
      </aside>

      {/* Main Chat Canvas */}
      <section className="us-chat-main">
        {/* Header Bar */}
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border bg-surface/85 px-5 py-3.5 backdrop-blur-md">
          <div className="flex items-center gap-3">
            <BrandLogo size={34} />
            <div>
              <h1 className="m-0 text-base font-bold text-text">{user?.role === "Graduate" ? "AI Career Coach" : "UniSphere AI Assistant"}</h1>
              <p className="m-0 text-xs text-text-secondary">
                {user?.role === "Graduate"
                  ? "Uses your profile, CV and progress. Keyword search over your own documents."
                  : provider === "rag" || provider === "rag-local"
                  ? "Strict BM25 Course RAG + Live UniSphere Context"
                  : "Hybrid University Context + General AI Knowledge"}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            {providers.length > 0 ? (
              <select
                value={provider}
                onChange={(e) => {
                  setProvider(e.target.value);
                  try {
                    localStorage.setItem(PROVIDER_KEY, e.target.value);
                  } catch {
                    // ignore
                  }
                }}
                aria-label="AI Mode"
                className="rounded-control border border-border bg-bg px-3 py-1.5 text-xs font-semibold text-text outline-none focus:border-primary"
              >
                {providers.map((p) => (
                  <option key={p.name} value={p.name}>
                    {p.displayName} ({p.model})
                  </option>
                ))}
              </select>
            ) : (
              <span className="text-xs font-medium text-warning">AI key required</span>
            )}
          </div>
        </div>

        {/* Error / Setup Notice */}
        {(error || providersReason) && (
          <div className="mx-5 mt-3 rounded-control border border-danger/30 bg-danger-light px-4 py-2.5 text-xs font-medium text-danger">
            {error || providersReason}
          </div>
        )}

        {/* Messages Viewport */}
        <div ref={messagesElRef} className="flex-1 overflow-y-auto px-5 py-6">
          {messages === null ? (
            <div className="py-12 text-center text-sm text-text-muted">Loading messages…</div>
          ) : messages.length === 0 && !streaming ? (
            <div className="mx-auto flex max-w-2xl flex-col items-center py-10 text-center">
              <BrandLogo size={58} className="mb-4" />
              <h2 className="m-0 text-xl font-bold text-text">
                How can I help you today, {user?.firstName || "there"}?
              </h2>
              <p className="mt-2 max-w-md text-sm text-text-secondary">
                Ask about your university courses, lecture notes, exams, coding problems, or career roadmap.
              </p>
              <div className="mt-6 grid w-full grid-cols-1 gap-2.5 sm:grid-cols-2">
                {quickPrompts.map((prompt) => (
                  <button
                    key={prompt}
                    type="button"
                    disabled={!provider}
                    onClick={() => void send(false, prompt)}
                    className="card-lift cursor-pointer rounded-card border border-border bg-surface p-3.5 text-start text-xs font-medium text-text transition-all hover:border-primary/50 hover:bg-primary-light/40 disabled:opacity-50"
                  >
                    <span className="mb-1 inline-flex text-primary"><UiIcon name="sparkles" /></span>
                    <div className="leading-snug">{prompt}</div>
                  </button>
                ))}
              </div>
            </div>
          ) : (
            <div className="mx-auto flex max-w-3xl flex-col gap-5">
              {messages.map((m, idx) => {
                const isUser = m.role === "user";
                return (
                  <div key={m.id || idx} className={`flex items-start gap-3 ${isUser ? "flex-row-reverse" : ""}`}>
                    {isUser ? (
                      <UserAvatar fullName={user?.fullName} avatarUrl={user?.avatarUrl} size={34} />
                    ) : (
                      <BrandLogo size={34} />
                    )}
                    <div className={`max-w-[82%] ${isUser ? "us-bubble-user" : "us-bubble-ai"}`}>
                      {isUser ? (
                        <div className="whitespace-pre-wrap text-sm leading-relaxed">{m.content}</div>
                      ) : (
                        <>
                          <div className="us-md text-sm leading-relaxed">
                            <Markdown text={m.content} />
                          </div>
                          {m.sources && m.sources.length > 0 && (
                            <div className="mt-3 flex flex-wrap items-center gap-1.5 border-t border-border/70 pt-2.5">
                              {m.sources.map((src, sIdx) => (
                                <button
                                  key={sIdx}
                                  type="button"
                                  onClick={() => src.kind === "material" && navigate("/university")}
                                  className="inline-flex cursor-pointer items-center gap-1.5 rounded-full border border-primary/25 bg-primary-light/70 px-2.5 py-0.5 text-[11px] font-medium text-primary transition-colors hover:bg-primary-light"
                                >
                                  <UiIcon name={src.kind === "material" ? "book-open" : "sparkles"} />
                                  {src.courseCode ? `${src.courseCode} · ` : ""}
                                  {src.title}
                                </button>
                              ))}
                            </div>
                          )}
                          <div className="mt-2 flex items-center justify-between gap-3 text-[11px] text-text-muted">
                            <span>{localTime(m.createdAtUtc)}</span>
                            <div className="flex items-center gap-2">
                              <button
                                type="button"
                                onClick={() => {
                                  void copyToClipboard(m.content);
                                  setCopiedIdx(idx);
                                  window.setTimeout(() => setCopiedIdx(null), 1500);
                                }}
                                className="cursor-pointer border-0 bg-transparent text-[11px] font-medium text-text-secondary hover:text-primary"
                              >
                                {copiedIdx === idx ? "Copied" : "Copy"}
                              </button>
                              {idx === messages.length - 1 && !streaming && (
                                <button
                                  type="button"
                                  onClick={() => void send(true)}
                                  className="inline-flex cursor-pointer items-center gap-1 border-0 bg-transparent text-[11px] font-medium text-text-secondary hover:text-primary"
                                >
                                  <UiIcon name="refresh-cw" /> Regenerate
                                </button>
                              )}
                            </div>
                          </div>
                        </>
                      )}
                    </div>
                  </div>
                );
              })}

              {/* Streaming Bubble with Animated Typing Dots */}
              {streaming && (
                <div className="flex items-start gap-3">
                  <BrandLogo size={34} />
                  <div className="us-bubble-ai max-w-[82%]">
                    {streamingText ? (
                      <div className="us-md text-sm leading-relaxed">
                        <Markdown text={streamingText} />
                      </div>
                    ) : (
                      <div className="flex items-center gap-1.5 py-1.5" aria-label="Assistant is typing">
                        <span className="us-typing-dot" />
                        <span className="us-typing-dot" style={{ animationDelay: "0.18s" }} />
                        <span className="us-typing-dot" style={{ animationDelay: "0.36s" }} />
                      </div>
                    )}
                    {streamingSources.length > 0 && (
                      <div className="mt-2.5 flex flex-wrap gap-1.5 border-t border-border/60 pt-2">
                        {streamingSources.map((src, sIdx) => (
                          <span
                            key={sIdx}
                            className="inline-flex items-center gap-1 rounded-full border border-primary/25 bg-primary-light/60 px-2.5 py-0.5 text-[11px] font-medium text-primary"
                          >
                            <UiIcon name={src.kind === "material" ? "book-open" : "sparkles"} />
                            {src.courseCode ? `${src.courseCode} · ` : ""}
                            {src.title}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Floating Composer Bar */}
        <div className="border-t border-border bg-surface/85 px-4 py-3.5 backdrop-blur-md">
          <div className="us-chat-composer mx-auto max-w-3xl rounded-2xl border border-border bg-bg shadow-card focus-within:border-primary">
            <textarea
              rows={1}
              value={input}
              onChange={(e) => {
                setInput(e.target.value);
                e.target.style.height = "auto";
                e.target.style.height = `${Math.min(150, Math.max(44, e.target.scrollHeight))}px`;
              }}
              onKeyDown={handleKeyDown}
              placeholder={
                provider === "rag"
                  ? "Ask about your UniSphere courses, notes, or grades…"
                  : provider
                    ? "Ask about courses, exams, code, or general topics…"
                    : "No AI provider is available…"
              }
              disabled={!provider || streaming}
              className="us-chat-textarea"
            />
            {streaming ? (
              <button
                type="button"
                onClick={() => abortRef.current?.abort()}
                className="inline-flex h-10 shrink-0 cursor-pointer items-center gap-1.5 rounded-xl border border-danger/30 bg-danger-light px-4 text-xs font-semibold text-danger"
              >
                Stop
              </button>
            ) : (
              <button
                type="button"
                onClick={() => void send(false)}
                disabled={!provider || !input.trim()}
                className="btn-luminous inline-flex h-10 shrink-0 cursor-pointer items-center gap-1.5 rounded-xl px-4 text-xs font-semibold"
              >
                Send <UiIcon name="send" />
              </button>
            )}
          </div>
        </div>
      </section>

      {/* Delete Conversation Modal */}
      {confirmDeleteId !== null && (
        <div className="fixed inset-0 z-100 flex items-center justify-center bg-black/45 p-4 backdrop-blur-xs">
          <div className="w-full max-w-sm rounded-card border border-border bg-surface p-5 shadow-card-lg">
            <h3 className="m-0 text-base font-bold text-text">Delete conversation?</h3>
            <p className="mt-2 mb-4 text-sm text-text-secondary">This permanently removes all messages in this chat.</p>
            <div className="flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setConfirmDeleteId(null)}
                className="cursor-pointer rounded-control border border-border bg-bg px-3.5 py-2 text-xs font-semibold text-text"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={() => void deleteConfirmed()}
                className="cursor-pointer rounded-control border-0 bg-danger px-3.5 py-2 text-xs font-semibold text-white"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
