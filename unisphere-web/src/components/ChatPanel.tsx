// AI chat (port of Components/ChatPanel.razor) on top of /api/chat.
//
// POST /api/chat/conversations/{id|new}/messages streams Server-Sent Events:
//   data: "<json string token>"      – appended to the reply
//   data: [DONE]                     – end of answer
//   event: error / data: {"error"}   – provider error with a safe message
// The new conversation id comes back in the X-Conversation-Id header.

import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { Icon } from './Icon';
import { Markdown } from './Markdown';
import { ApiError, api, errorMessage, getToken, url } from '../lib/api';
import { fmtTime } from '../lib/format';
import type { ChatConversationDetailDto, ChatConversationDto, ChatMessageDto, ChatProviderDto, ChatProviderStatusDto } from '../types/dto';

const PROVIDER_KEY = 'unisphere.chat.provider';
const FALLBACK_ERROR = "The assistant couldn't complete the reply. Please try again.";

function storedProvider(): string | null {
  try {
    return localStorage.getItem(PROVIDER_KEY);
  } catch {
    return null;
  }
}

function extractError(payload: string): string {
  try {
    const v = JSON.parse(payload) as unknown;
    if (typeof v === 'string') return v || FALLBACK_ERROR;
    if (v && typeof v === 'object') {
      const o = v as Record<string, unknown>;
      if (typeof o.error === 'string') return o.error;
      if (o.error && typeof o.error === 'object' && typeof (o.error as Record<string, unknown>).message === 'string')
        return (o.error as Record<string, string>).message;
      if (typeof o.message === 'string') return o.message;
    }
  } catch {
    /* malformed */
  }
  return FALLBACK_ERROR;
}

interface ChatPanelProps {
  showConversations?: boolean;
  placeholder?: string;
  emptyText?: string;
}

export function ChatPanel({
  showConversations = true,
  placeholder = 'Ask about your courses…',
  emptyText = 'Ask about your courses, study plan or grades — I reply with your own materials as context.',
}: ChatPanelProps) {
  const [providers, setProviders] = useState<ChatProviderDto[]>([]);
  const [provider, setProvider] = useState('');
  const [providersReason, setProvidersReason] = useState('');
  const [conversations, setConversations] = useState<ChatConversationDto[] | null>(null);
  const [activeId, setActiveId] = useState<number | null>(null);
  const [messages, setMessages] = useState<ChatMessageDto[] | null>(null);
  const [input, setInput] = useState('');
  const [streaming, setStreaming] = useState(false);
  const [streamText, setStreamText] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<number | null>(null);
  const [renameId, setRenameId] = useState<number | null>(null);
  const [renameText, setRenameText] = useState('');
  const abortRef = useRef<AbortController | null>(null);
  const logRef = useRef<HTMLDivElement>(null);
  const stickToBottom = useRef(true);

  const activeModel = providers.find((p) => p.name === provider)?.model ?? '';

  const loadConversations = useCallback(async (selectMostRecent: boolean) => {
    try {
      const list = (await api.get<ChatConversationDto[]>('/api/chat/conversations')) ?? [];
      setConversations(list);
      return list;
    } catch {
      setConversations([]);
      return [];
    } finally {
      void selectMostRecent;
    }
  }, []);

  const selectConversation = useCallback(async (id: number) => {
    setActiveId(id);
    setMessages(null);
    setError(null);
    try {
      const detail = await api.get<ChatConversationDetailDto>(`/api/chat/conversations/${id}`);
      setMessages(detail?.messages ?? []);
    } catch {
      setError('Could not load this conversation.');
      setMessages([]);
    }
  }, []);

  // Initial load: providers (+ reason when none), conversations, most recent chat.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = (await api.get<ChatProviderDto[]>('/api/chat/providers')) ?? [];
        if (cancelled) return;
        setProviders(list);
        const saved = storedProvider();
        setProvider(list.some((p) => p.name === saved) ? (saved as string) : (list[0]?.name ?? ''));
        if (list.length === 0) {
          try {
            const statuses = await api.get<ChatProviderStatusDto[]>('/api/chat/providers/status');
            const reasons = (statuses ?? []).filter((s) => !s.available && s.reason).map((s) => s.reason);
            if (!cancelled) setProvidersReason(reasons.join(' '));
          } catch {
            /* ignore */
          }
        }
      } catch (err) {
        if (!cancelled) setError(err instanceof ApiError && err.status === 401 ? 'Session expired. Please sign in again.' : 'Could not load AI providers.');
      }
      const convs = await loadConversations(true);
      if (cancelled) return;
      if (showConversations && convs.length > 0) await selectConversation(convs[0].id);
      else setMessages([]);
    })();
    return () => {
      cancelled = true;
      abortRef.current?.abort();
    };
  }, [loadConversations, selectConversation, showConversations]);

  // Keep the newest message in view unless the user scrolled up.
  useEffect(() => {
    const el = logRef.current;
    if (el && stickToBottom.current) el.scrollTop = el.scrollHeight;
  }, [messages, streamText]);

  const onScroll = () => {
    const el = logRef.current;
    if (el) stickToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80;
  };

  const changeProvider = (name: string) => {
    setProvider(name);
    try {
      localStorage.setItem(PROVIDER_KEY, name);
    } catch {
      /* ignore */
    }
  };

  const newChat = () => {
    setActiveId(null);
    setMessages([]);
    setError(null);
    setInput('');
  };

  const send = async (regenerate: boolean) => {
    const text = regenerate ? ([...(messages ?? [])].reverse().find((m) => m.role === 'user')?.content ?? '').trim() : input.trim();
    if (!text || streaming || !provider) return;
    setError(null);
    if (!regenerate) setInput('');
    setStreaming(true);
    setStreamText('');
    stickToBottom.current = true;
    const providerName = provider;
    const model = activeModel;
    const now = new Date().toISOString();

    let base = [...(messages ?? [])];
    if (regenerate && base.length > 0 && base[base.length - 1].role === 'assistant') base = base.slice(0, -1);
    if (!regenerate) base.push({ id: 0, role: 'user', content: text, provider: providerName, model, createdAtUtc: now });
    setMessages(base);

    const controller = new AbortController();
    abortRef.current = controller;
    let reply = '';
    const knownIds = new Set((conversations ?? []).map((c) => c.id));
    try {
      const conv = activeId ?? 'new';
      const headers: Record<string, string> = { 'Content-Type': 'application/json', Accept: 'text/event-stream' };
      const token = getToken();
      if (token) headers.Authorization = `Bearer ${token}`;
      const res = await fetch(url(`/api/chat/conversations/${conv}/messages${regenerate ? '?regenerate=true' : ''}`), {
        method: 'POST',
        headers,
        body: JSON.stringify({ provider: providerName, content: text }),
        signal: controller.signal,
      });
      if (!res.ok) {
        let msg = '';
        try {
          const body = (await res.json()) as Record<string, unknown>;
          msg = typeof body.message === 'string' ? body.message : typeof body.title === 'string' ? body.title : '';
          const errs = body.errors as Record<string, string[]> | undefined;
          if (errs) msg = Object.values(errs).flat()[0] ?? msg;
        } catch {
          /* ignore */
        }
        if (res.status === 401) msg = 'Session expired. Please sign in again.';
        if (!msg) msg = res.status === 429 ? 'Too many messages. Please wait a bit and try again.' : 'The assistant is unavailable right now. Please try again later.';
        setError(msg);
        if (!regenerate) setInput(text);
        setMessages((m) => (m && !regenerate ? m.filter((x) => !(x.id === 0 && x.role === 'user' && x.createdAtUtc === now)) : m));
        return;
      }

      const headerId = Number(res.headers.get('X-Conversation-Id'));
      if (headerId) setActiveId(headerId);

      const reader = res.body!.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      let event: string | null = null;
      let done = false;
      while (!done) {
        const chunk = await reader.read();
        if (chunk.done) break;
        buffer += decoder.decode(chunk.value, { stream: true });
        let nl: number;
        while ((nl = buffer.indexOf('\n')) >= 0) {
          const line = buffer.slice(0, nl).replace(/\r$/, '');
          buffer = buffer.slice(nl + 1);
          if (line.startsWith('event:')) {
            event = line.slice(6).trim();
            continue;
          }
          if (!line.startsWith('data:')) {
            if (!line) event = null;
            continue;
          }
          const payload = line.slice(5).trim();
          if (payload === '[DONE]') {
            done = true;
            break;
          }
          if (event === 'error') {
            setError(extractError(payload));
            done = true;
            break;
          }
          try {
            const tok = JSON.parse(payload) as unknown;
            if (typeof tok === 'string') {
              reply += tok;
              setStreamText(reply);
            }
          } catch {
            /* skip malformed token */
          }
        }
      }
      if (done) controller.abort();
    } catch (err) {
      if (!(err instanceof DOMException && err.name === 'AbortError')) setError("Couldn't reach the assistant. Please try again.");
    } finally {
      if (reply) setMessages((m) => [...(m ?? []), { id: 0, role: 'assistant', content: reply, provider: providerName, model, createdAtUtc: new Date().toISOString() }]);
      setStreamText('');
      setStreaming(false);
      abortRef.current = null;
      // Refresh the sidebar; without the header (cross-origin) pick the new conversation.
      const list = await loadConversations(false);
      setActiveId((current) => current ?? list.find((c) => !knownIds.has(c.id))?.id ?? null);
    }
  };

  const stop = () => abortRef.current?.abort();

  const copy = (text: string) => {
    void navigator.clipboard?.writeText(text).catch(() => undefined);
  };

  const deleteConfirmed = async () => {
    if (confirmDeleteId === null) return;
    try {
      await api.del(`/api/chat/conversations/${confirmDeleteId}`);
      if (activeId === confirmDeleteId) newChat();
      await loadConversations(false);
    } catch (err) {
      setError(errorMessage(err, 'Could not delete the conversation.'));
    } finally {
      setConfirmDeleteId(null);
    }
  };

  const rename = async (id: number) => {
    const title = renameText.trim();
    setRenameId(null);
    if (!title) return;
    try {
      await api.patch(`/api/chat/conversations/${id}`, { title });
      await loadConversations(false);
    } catch (err) {
      setError(errorMessage(err, 'Could not rename the conversation.'));
    }
  };

  const onInputKey = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      void send(false);
    }
  };

  const canRegenerate = !streaming && !!messages && messages.length > 0 && messages[messages.length - 1].role === 'assistant';

  return (
    <div className="chat-root">
      {showConversations && (
        <aside className="chat-side">
          <button className="btn btn-primary btn-sm chat-new" onClick={newChat} disabled={streaming}>
            <Icon name="plus" /> New chat
          </button>
          {conversations === null ? (
            <div className="loading">Loading…</div>
          ) : conversations.length === 0 ? (
            <div className="empty-state">
              <Icon name="message-square" /> No conversations yet.
            </div>
          ) : (
            conversations.map((c) => (
              <div key={c.id} className={`chat-conv-item ${activeId === c.id ? 'selected' : ''}`}>
                {renameId === c.id ? (
                  <>
                    <input
                      value={renameText}
                      onChange={(e) => setRenameText(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') void rename(c.id);
                        else if (e.key === 'Escape') setRenameId(null);
                      }}
                      maxLength={100}
                      aria-label="Conversation title"
                      autoFocus
                    />
                    <button className="btn btn-sm btn-primary" onClick={() => void rename(c.id)}>
                      Save
                    </button>
                  </>
                ) : (
                  <>
                    <button className="chat-conv-title" onClick={() => void selectConversation(c.id)} title={c.title} disabled={streaming}>
                      {c.title}
                    </button>
                    <button
                      className="icon-btn"
                      title="Rename"
                      aria-label="Rename conversation"
                      onClick={() => {
                        setRenameId(c.id);
                        setRenameText(c.title);
                      }}
                    >
                      <Icon name="pencil" />
                    </button>
                    <button className="icon-btn icon-btn-danger" title="Delete" aria-label="Delete conversation" onClick={() => setConfirmDeleteId(c.id)}>
                      <Icon name="trash-2" />
                    </button>
                  </>
                )}
              </div>
            ))
          )}
        </aside>
      )}

      <section className="chat-main">
        <div className="chat-toolbar">
          <select
            className="chat-provider"
            value={provider}
            onChange={(e) => changeProvider(e.target.value)}
            disabled={streaming || providers.length === 0}
            aria-label="AI provider"
          >
            {providers.length === 0 && <option value="">No AI provider configured</option>}
            {providers.map((p, pIdx) => (
              <option key={`${p.name}-${pIdx}`} value={p.name}>
                {p.displayName} ({p.model})
              </option>
            ))}
          </select>
          <span className="chat-toolbar-spacer" />
          {confirmDeleteId !== null ? (
            <span className="chat-confirm">
              Delete this chat?{' '}
              <button className="btn btn-sm btn-danger" onClick={() => void deleteConfirmed()}>
                Delete
              </button>{' '}
              <button className="btn btn-sm btn-secondary" onClick={() => setConfirmDeleteId(null)}>
                Cancel
              </button>
            </span>
          ) : (
            <>
              {!showConversations && (
                <button className="btn btn-sm btn-secondary" onClick={newChat} disabled={streaming}>
                  <Icon name="plus" /> New chat
                </button>
              )}
              {activeId !== null && messages && messages.length > 0 && (
                <button className="btn btn-sm btn-secondary" onClick={() => setConfirmDeleteId(activeId)} disabled={streaming}>
                  Delete chat
                </button>
              )}
              {canRegenerate && (
                <button className="btn btn-sm btn-secondary chat-regen" onClick={() => void send(true)}>
                  <Icon name="refresh-cw" /> Regenerate
                </button>
              )}
            </>
          )}
        </div>

        {error && <div className="alert alert-error">{error}</div>}
        {providers.length === 0 && providersReason && <div className="alert alert-error">{providersReason}</div>}

        <div className="chat-messages" ref={logRef} onScroll={onScroll} role="log" aria-live="polite">
          {messages === null ? (
            <div className="loading">Loading messages…</div>
          ) : messages.length === 0 && !streaming ? (
            <div className="empty-state">{emptyText}</div>
          ) : (
            <>
              {messages.map((m, i) => (
                <div key={`${m.id}-${i}`} className={`chat-msg ${m.role === 'user' ? 'chat-user' : 'chat-assistant'}`}>
                  <div className="chat-bubble" dir="auto">
                    {m.role === 'user' ? <p dir="auto">{m.content}</p> : <Markdown text={m.content} onCopy={copy} />}
                  </div>
                  <div className="chat-meta">
                    {m.role === 'assistant' && m.provider && (
                      <>
                        <span className="chat-meta-provider">{m.provider}</span>
                        <span className="chat-meta-model">{m.model}</span>
                      </>
                    )}
                    <span className="chat-meta-time">{fmtTime(m.createdAtUtc)}</span>
                    {m.role === 'assistant' && (
                      <button className="chat-copy" onClick={() => copy(m.content)}>
                        Copy
                      </button>
                    )}
                  </div>
                </div>
              ))}
              {streaming && (
                <div className="chat-msg chat-assistant">
                  <div className="chat-bubble" dir="auto">
                    {streamText ? (
                      <Markdown text={streamText} />
                    ) : (
                      <span className="typing" aria-label="Assistant is typing">
                        <span />
                        <span />
                        <span />
                      </span>
                    )}
                  </div>
                  {streamText && (
                    <div className="chat-meta">
                      <span className="chat-meta-provider">{provider}</span>
                      <span className="chat-meta-model">{activeModel}</span>
                    </div>
                  )}
                </div>
              )}
            </>
          )}
        </div>

        <div className="chat-input-row">
          <textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={onInputKey}
            rows={2}
            placeholder={placeholder}
            aria-label="Message"
            maxLength={2000}
            disabled={streaming || !provider}
          />
          {streaming ? (
            <button className="btn btn-danger chat-send" onClick={stop}>
              Stop
            </button>
          ) : (
            <button className="btn btn-primary chat-send" onClick={() => void send(false)} disabled={!input.trim() || !provider}>
              Send
            </button>
          )}
        </div>
        <p className="chat-note">AI can make mistakes. Check important information.</p>
      </section>
    </div>
  );
}
