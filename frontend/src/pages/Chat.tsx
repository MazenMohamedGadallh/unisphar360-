import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Markdown } from "../markdown";
import { copyToClipboard } from "../clipboard";
import { UiIcon } from "../UiIcon";
import "../chat.css";

/*
 * React port of Components/ChatPanel.razor - same design, class names and
 * behaviour: conversations sidebar, grouped provider dropdown (RAG Mode /
 * Direct AI), SSE streaming via fetch + ReadableStream, Stop, Regenerate,
 * markdown bubbles, rename/delete. Auth guard mirrors the Blazor
 * [Authorize(Roles = "Student,Doctor,Graduate")] gate on Pages/Chat.razor.
 */

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";
const TOKEN_KEY = "unisphere.auth.token";
const PROVIDER_KEY = "unisphere.chat.provider";

const FALLBACK_ASSISTANT_ERROR = "The assistant couldn't complete the reply. Please try again.";
const ALLOWED_ROLES = ["student", "doctor", "graduate"];

interface ChatProvider {
  name: string;
  displayName: string;
  model: string;
}

interface ChatProviderStatus extends ChatProvider {
  available: boolean;
  reason: string;
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
}

interface ChatConversationDetail {
  id: number;
  title: string;
  provider: string;
  updatedAtUtc: string;
  messages: ChatMessage[];
}

interface AuthUser {
  role: string;
}

function getToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY);
  } catch {
    return null;
  }
}

/** Same shapes the Blazor ExtractErrorMessage helper accepts. */
function extractErrorMessage(payload: string): string {
  try {
    const doc: unknown = JSON.parse(payload);
    if (typeof doc === "string") return doc || FALLBACK_ASSISTANT_ERROR;
    if (doc && typeof doc === "object") {
      const obj = doc as Record<string, unknown>;
      if (typeof obj.error === "string") return obj.error;
      if (obj.error && typeof obj.error === "object") {
        const nested = obj.error as Record<string, unknown>;
        if (typeof nested.message === "string") return nested.message;
      }
      if (typeof obj.message === "string") return obj.message;
    }
  } catch {
    // Malformed payload: fall through to the generic message.
  }
  return FALLBACK_ASSISTANT_ERROR;
}

function localTime(isoUtc: string): string {
  return new Date(isoUtc).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
}

export function Chat() {
  const navigate = useNavigate();

  // Auth guard (Blazor: @attribute [Authorize(Roles = "...")])
  const [authChecked, setAuthChecked] = useState(false);

  const [providers, setProviders] = useState<ChatProvider[]>([]);
  const [provider, setProvider] = useState("");
  const [providersEmptyText, setProvidersEmptyText] = useState("No AI provider configured");
  const [providersReason, setProvidersReason] = useState("");

  const [conversations, setConversations] = useState<ChatConversation[] | null>(null);
  const [activeId, setActiveId] = useState<number | null>(null);
  const [messages, setMessages] = useState<ChatMessage[] | null>(null);
  const [input, setInput] = useState("");
  const [streaming, setStreaming] = useState(false);
  const [streamingText, setStreamingText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<number | null>(null);
  const [renameId, setRenameId] = useState<number | null>(null);
  const [renameText, setRenameText] = useState("");

  // Refs mirror the Blazor component fields: the async send loop reads them
  // the way the C# code reads its fields, without stale-render problems.
  const messagesRef = useRef<ChatMessage[] | null>(null);
  const activeIdRef = useRef<number | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const messagesElRef = useRef<HTMLDivElement>(null);
  const lastScrollRef = useRef(0);

  function applyMessages(list: ChatMessage[] | null) {
    messagesRef.current = list;
    setMessages(list);
  }

  function applyActiveId(id: number | null) {
    activeIdRef.current = id;
    setActiveId(id);
  }

  const activeProviderModel =
    providers.find((p) => p.name === provider)?.model ?? "";

  const canRegenerate =
    !streaming &&
    messages !== null &&
    messages.length > 0 &&
    messages[messages.length - 1].role === "assistant";

  // ---- Auth guard -------------------------------------------------------
  useEffect(() => {
    let cancelled = false;
    const token = getToken();
    if (!token) {
      navigate("/login", { replace: true });
      return;
    }
    void (async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/api/auth/me`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!response.ok) throw new Error("unauthorized");
        const user = (await response.json()) as AuthUser;
        const role = (user.role ?? "").trim().toLowerCase();
        if (!ALLOWED_ROLES.includes(role)) {
          if (!cancelled) navigate("/login", { replace: true });
          return;
        }
        if (!cancelled) setAuthChecked(true);
      } catch {
        // Missing/expired token: the login page shows the session notice.
        if (!cancelled) navigate("/login?expired=1", { replace: true });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [navigate]);

  // ---- Initial data load (after the auth guard passes) -------------------
  useEffect(() => {
    if (!authChecked) return;
    void loadProviders();
    void loadConversations(true);
    // Empty deps on purpose: runs once when the guard flips to true.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authChecked]);

  // Stop any in-flight stream when the page unmounts (Blazor DisposeAsync).
  useEffect(() => () => abortRef.current?.abort(), []);

  // ---- Data loading ------------------------------------------------------
  async function authHeaders(): Promise<Record<string, string>> {
    return { Authorization: `Bearer ${getToken() ?? ""}` };
  }

  async function loadProviders() {
    try {
      const response = await fetch(`${API_BASE_URL}/api/chat/providers`, {
        headers: await authHeaders(),
      });
      if (response.status === 401) {
        setError("Session expired. Please sign in again.");
        return;
      }
      if (!response.ok) throw new Error("providers failed");
      const list = (await response.json()) as ChatProvider[];
      setProviders(list);

      // Restore the last selection (unisphere.chat.provider) when still valid.
      let stored: string | null = null;
      try {
        stored = localStorage.getItem(PROVIDER_KEY);
      } catch {
        // Storage unavailable: best effort only.
      }
      setProvider(
        stored && list.some((p) => p.name === stored)
          ? stored
          : list[0]?.name ?? "",
      );

      if (list.length === 0) await loadProvidersReason();
      else {
        setProvidersReason("");
        setProvidersEmptyText("No AI provider configured");
      }
    } catch {
      setError("Could not load AI providers.");
    }
  }

  async function loadProvidersReason() {
    try {
      const response = await fetch(`${API_BASE_URL}/api/chat/providers/status`, {
        headers: await authHeaders(),
      });
      if (!response.ok) throw new Error("status failed");
      const statuses = (await response.json()) as ChatProviderStatus[];
      const reasons = statuses
        .filter((s) => !s.available && s.reason.trim().length > 0)
        .map((s) => s.reason);
      setProvidersReason(reasons.join(" "));
      setProvidersEmptyText(
        reasons.length > 0
          ? "No AI provider configured — " + reasons.join("; ")
          : "No AI provider configured",
      );
    } catch {
      setProvidersReason("");
      setProvidersEmptyText("No AI provider configured");
    }
  }

  async function loadConversations(selectMostRecent: boolean) {
    try {
      const response = await fetch(`${API_BASE_URL}/api/chat/conversations`, {
        headers: await authHeaders(),
      });
      if (!response.ok) throw new Error("conversations failed");
      const list = (await response.json()) as ChatConversation[];
      setConversations(list);
      if (selectMostRecent && activeIdRef.current === null && list.length > 0) {
        await selectConversation(list[0].id);
      } else if (
        activeIdRef.current !== null &&
        !list.some((c) => c.id === activeIdRef.current)
      ) {
        applyActiveId(null);
      }
    } catch {
      setConversations([]);
    }
  }

  async function selectConversation(id: number) {
    applyActiveId(id);
    applyMessages(null);
    setError(null);
    try {
      const response = await fetch(
        `${API_BASE_URL}/api/chat/conversations/${id}`,
        { headers: await authHeaders() },
      );
      if (!response.ok) throw new Error("conversation failed");
      const detail = (await response.json()) as ChatConversationDetail;
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

  function providerChanged(name: string) {
    setProvider(name);
    try {
      localStorage.setItem(PROVIDER_KEY, name);
    } catch {
      // Storage unavailable: selection just won't persist.
    }
  }

  // ---- Streaming send ----------------------------------------------------
  async function send(regenerate: boolean) {
    const text = regenerate
      ? (messagesRef.current
          ?.filter((m) => m.role === "user")
          .pop()?.content ?? "").trim()
      : input.trim();
    if (!text || streaming || !provider) return;

    setError(null);
    if (!regenerate) setInput("");
    setStreaming(true);
    setStreamingText("");
    const controller = new AbortController();
    abortRef.current = controller;

    const providerName = provider;
    const providerModel = activeProviderModel;
    let accumulated = "";

    try {
      let list = messagesRef.current ?? [];
      if (
        regenerate &&
        list.length > 0 &&
        list[list.length - 1].role === "assistant"
      ) {
        list = list.slice(0, -1);
        applyMessages(list);
      }

      const convId = activeIdRef.current?.toString() ?? "new";
      const url =
        `${API_BASE_URL}/api/chat/conversations/${convId}/messages` +
        (regenerate ? "?regenerate=true" : "");
      const response = await fetch(url, {
        method: "POST",
        headers: {
          ...(await authHeaders()),
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ provider: providerName, content: text }),
        signal: controller.signal,
      });

      if (!response.ok) {
        await handleSendError(response);
        return;
      }

      const conversationHeader = response.headers.get("X-Conversation-Id");
      const newId = conversationHeader === null ? Number.NaN : Number(conversationHeader);
      if (!Number.isNaN(newId)) {
        applyActiveId(newId);
        await loadConversations(false);
      }

      if (!regenerate || !list.some((m) => m.role === "user")) {
        list = [
          ...list,
          {
            id: 0,
            role: "user",
            content: text,
            provider: providerName,
            model: providerModel,
            createdAtUtc: new Date().toISOString(),
          },
        ];
        applyMessages(list);
      }

      // SSE: data: <json token> frames, data: [DONE], event: error frames.
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

        let newline = buffer.indexOf("\n");
        while (newline >= 0) {
          const line = buffer.slice(0, newline).replace(/\r$/, "");
          buffer = buffer.slice(newline + 1);
          newline = buffer.indexOf("\n");

          if (line.startsWith("event:")) {
            currentEvent = line.slice("event:".length).trim();
            continue;
          }
          if (!line.startsWith("data:")) continue;
          const payload = line.slice("data:".length).trim();
          if (payload === "[DONE]") {
            done = true;
            break;
          }
          if (currentEvent === "error") {
            setError(extractErrorMessage(payload));
            done = true;
            break;
          }
          try {
            const token: unknown = JSON.parse(payload);
            if (typeof token === "string" && token.length > 0) {
              accumulated += token;
              setStreamingText(accumulated);
              maybeAutoScroll();
            }
          } catch {
            // Skip malformed token frames.
          }
        }
      }

      if (accumulated.length > 0) {
        applyMessages([
          ...(messagesRef.current ?? []),
          {
            id: 0,
            role: "assistant",
            content: accumulated,
            provider: providerName,
            model: providerModel,
            createdAtUtc: new Date().toISOString(),
          },
        ]);
      }
      setStreamingText("");
      maybeAutoScroll();
    } catch (err) {
      const aborted =
        err instanceof DOMException
          ? err.name === "AbortError"
          : err instanceof Error && err.name === "AbortError";
      if (aborted) {
        // Stopped by the user: the server keeps the partial answer, so show
        // the same partial text here instead of dropping it.
        if (accumulated.length > 0) {
          applyMessages([
            ...(messagesRef.current ?? []),
            {
              id: 0,
              role: "assistant",
              content: accumulated,
              provider: providerName,
              model: providerModel,
              createdAtUtc: new Date().toISOString(),
            },
          ]);
        }
        setStreamingText("");
      } else if (err instanceof TypeError) {
        setError("Couldn't reach the assistant. Please try again.");
      } else {
        setError("Couldn't reach the assistant. Please try again.");
      }
    } finally {
      setStreaming(false);
      abortRef.current = null;
    }
  }

  /** Maps non-2xx send responses to the same messages as Blazor. */
  async function handleSendError(response: Response) {
    if (response.status === 401) {
      setError("Session expired. Please sign in again.");
      return;
    }
    try {
      const body = (await response.json()) as { message?: unknown };
      if (typeof body?.message === "string" && body.message.length > 0) {
        setError(body.message);
        return;
      }
    } catch {
      // No JSON body (e.g. rate-limiter rejection): fall through.
    }
    setError(
      response.status === 429
        ? "Too many messages. Please wait a bit and try again."
        : "The assistant is unavailable right now. Please try again later.",
    );
  }

  function stop() {
    abortRef.current?.abort();
  }

  // ---- Auto-scroll (same rule as wwwroot/js/chat.js) ---------------------
  function maybeAutoScroll() {
    const now = Date.now();
    if (now - lastScrollRef.current < 400) return;
    lastScrollRef.current = now;
    const el = messagesElRef.current;
    if (!el) return;
    if (el.scrollHeight - el.scrollTop - el.clientHeight < 80) {
      el.scrollTop = el.scrollHeight;
    }
  }

  // ---- Rename / delete ---------------------------------------------------
  function startRename(conversation: ChatConversation) {
    setRenameId(conversation.id);
    setRenameText(conversation.title);
  }

  async function renameConversation(id: number) {
    if (!renameText.trim()) {
      setRenameId(null);
      return;
    }
    try {
      const response = await fetch(
        `${API_BASE_URL}/api/chat/conversations/${id}`,
        {
          method: "PATCH",
          headers: {
            ...(await authHeaders()),
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ title: renameText.trim() }),
        },
      );
      if (!response.ok) setError("Could not rename the conversation.");
      await loadConversations(false);
    } catch {
      setError("Could not rename the conversation.");
    } finally {
      setRenameId(null);
    }
  }

  function renameKeyDown(event: KeyboardEvent<HTMLInputElement>, id: number) {
    if (event.key === "Enter") void renameConversation(id);
    else if (event.key === "Escape") setRenameId(null);
  }

  async function deleteConfirmed() {
    if (confirmDeleteId === null) return;
    try {
      const response = await fetch(
        `${API_BASE_URL}/api/chat/conversations/${confirmDeleteId}`,
        { method: "DELETE", headers: await authHeaders() },
      );
      if (!response.ok) {
        setError("Could not delete the conversation.");
        return;
      }
      if (activeIdRef.current === confirmDeleteId) {
        applyActiveId(null);
        applyMessages([]);
      }
      await loadConversations(false);
    } catch {
      setError("Could not delete the conversation.");
    } finally {
      setConfirmDeleteId(null);
    }
  }

  // ---- Input -------------------------------------------------------------
  function inputKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    // Enter sends, Shift+Enter inserts a newline (Blazor chat.js helper).
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      void send(false);
    }
  }

  // ---- Guard rendering ---------------------------------------------------
  if (!authChecked) {
    return (
      <div className="chat-page">
        <div className="loading">Loading…</div>
      </div>
    );
  }

  const ragProviders = providers.filter((p) => p.name === "rag");
  const directProviders = providers.filter((p) => p.name !== "rag");

  return (
    <div className="chat-page">
      <div className="page-header">
        <h1>AI Assistant</h1>
        <p>Chat with UniSphere Assistant about your courses and studies</p>
      </div>

      <div className="chat-root">
        <aside className="chat-side">
          <button
            type="button"
            className="btn btn-primary btn-sm chat-new"
            onClick={newChat}
            disabled={streaming}
          >
            <UiIcon name="plus" /> New chat
          </button>
          {conversations === null ? (
            <div className="loading">Loading…</div>
          ) : conversations.length === 0 ? (
            <div className="empty-state">
              <div className="empty-icon">
                <UiIcon name="message-square" />
              </div>
              No conversations yet.
            </div>
          ) : (
            conversations.map((c) => (
              <div
                key={c.id}
                className={`chat-conv-item${activeId === c.id ? " selected" : ""}`}
              >
                {renameId === c.id ? (
                  <>
                    <input
                      value={renameText}
                      onChange={(e) => setRenameText(e.target.value)}
                      onKeyDown={(e) => renameKeyDown(e, c.id)}
                      maxLength={100}
                      aria-label="Conversation title"
                    />
                    <button
                      type="button"
                      className="btn btn-sm btn-primary"
                      onClick={() => void renameConversation(c.id)}
                    >
                      Save
                    </button>
                  </>
                ) : (
                  <>
                    <button
                      type="button"
                      className="chat-conv-title"
                      onClick={() => void selectConversation(c.id)}
                      title={c.title}
                    >
                      {c.title}
                    </button>
                    <button
                      type="button"
                      className="icon-btn"
                      title="Rename"
                      aria-label="Rename conversation"
                      onClick={() => startRename(c)}
                    >
                      <UiIcon name="pencil" />
                    </button>
                    <button
                      type="button"
                      className="icon-btn icon-btn-danger"
                      title="Delete"
                      aria-label="Delete conversation"
                      onClick={() => setConfirmDeleteId(c.id)}
                    >
                      <UiIcon name="trash-2" />
                    </button>
                  </>
                )}
              </div>
            ))
          )}
        </aside>

        <section className="chat-main">
          <div className="chat-toolbar">
            <select
              className="chat-provider"
              value={provider}
              onChange={(e) => providerChanged(e.target.value)}
              disabled={streaming || providers.length === 0}
              aria-label="AI provider"
            >
              {providers.length === 0 && (
                <option value="">{providersEmptyText}</option>
              )}
              {ragProviders.length > 0 && (
                <optgroup label="RAG Mode">
                  {ragProviders.map((p) => (
                    <option key={p.name} value={p.name}>
                      {p.displayName} ({p.model})
                    </option>
                  ))}
                </optgroup>
              )}
              {directProviders.length > 0 && (
                <optgroup label="Direct AI">
                  {directProviders.map((p) => (
                    <option key={p.name} value={p.name}>
                      {p.displayName} ({p.model})
                    </option>
                  ))}
                </optgroup>
              )}
            </select>
            <span className="chat-toolbar-spacer" />
            {confirmDeleteId !== null ? (
              <span className="chat-confirm">
                Delete this chat?
                <button
                  type="button"
                  className="btn btn-sm btn-danger"
                  onClick={() => void deleteConfirmed()}
                >
                  Delete
                </button>
                <button
                  type="button"
                  className="btn btn-sm btn-secondary"
                  onClick={() => setConfirmDeleteId(null)}
                >
                  Cancel
                </button>
              </span>
            ) : (
              <>
                {activeId !== null &&
                  messages !== null &&
                  messages.length > 0 && (
                    <button
                      type="button"
                      className="btn btn-sm btn-secondary"
                      onClick={() => setConfirmDeleteId(activeId)}
                      disabled={streaming}
                    >
                      Delete chat
                    </button>
                  )}
                {canRegenerate && (
                  <button
                    type="button"
                    className="btn btn-sm btn-secondary chat-regen"
                    onClick={() => void send(true)}
                    disabled={streaming}
                  >
                    <UiIcon name="refresh-cw" /> Regenerate
                  </button>
                )}
              </>
            )}
          </div>

          {error && <div className="alert alert-error">{error}</div>}
          {providers.length === 0 && providersReason && (
            <div className="alert alert-error">{providersReason}</div>
          )}

          <div
            className="chat-messages"
            id="chat-messages"
            ref={messagesElRef}
            role="log"
            aria-live="polite"
          >
            {messages === null ? (
              <div className="loading">Loading messages…</div>
            ) : messages.length === 0 && !streaming ? (
              <div className="empty-state">
                Ask about your courses, study plan or grades — I reply with
                your own materials as context.
              </div>
            ) : (
              <>
                {messages.map((msg, index) => (
                  <div
                    key={`${msg.id}-${index}`}
                    className={`chat-msg ${msg.role === "user" ? "chat-user" : "chat-assistant"}`}
                  >
                    <div className="chat-bubble" dir="auto">
                      <Markdown text={msg.content} />
                    </div>
                    <div className="chat-meta">
                      {msg.role === "assistant" && msg.provider && (
                        <>
                          <span className="chat-meta-provider">
                            {msg.provider}
                          </span>
                          <span className="chat-meta-model">{msg.model}</span>
                        </>
                      )}
                      <span className="chat-meta-time">
                        {localTime(msg.createdAtUtc)}
                      </span>
                      {msg.role === "assistant" && (
                        <button
                          type="button"
                          className="chat-copy"
                          onClick={() => copyToClipboard(msg.content)}
                        >
                          Copy
                        </button>
                      )}
                    </div>
                  </div>
                ))}
                {streaming && (
                  <div className="chat-msg chat-assistant">
                    <div className="chat-bubble" dir="auto">
                      {streamingText ? (
                        <Markdown text={streamingText} />
                      ) : (
                        <span
                          className="typing"
                          aria-label="Assistant is typing"
                        >
                          <span />
                          <span />
                          <span />
                        </span>
                      )}
                    </div>
                    {streamingText && (
                      <div className="chat-meta">
                        <span className="chat-meta-provider">{provider}</span>
                        <span className="chat-meta-model">
                          {activeProviderModel}
                        </span>
                      </div>
                    )}
                  </div>
                )}
              </>
            )}
          </div>

          <div className="chat-input-row">
            <textarea
              rows={2}
              placeholder="Ask about your courses..."
              aria-label="Message"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={inputKeyDown}
              disabled={streaming || provider === ""}
            />
            {streaming ? (
              <button
                type="button"
                className="btn btn-danger chat-send"
                onClick={stop}
              >
                Stop
              </button>
            ) : (
              <button
                type="button"
                className="btn btn-primary chat-send"
                onClick={() => void send(false)}
                disabled={input.trim() === "" || provider === ""}
              >
                <UiIcon name="send" /> Send
              </button>
            )}
          </div>
          <p className="chat-note">
            AI can make mistakes. Check important information.
          </p>
        </section>
      </div>
    </div>
  );
}
