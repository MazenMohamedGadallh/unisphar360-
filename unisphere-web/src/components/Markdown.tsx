// Port of Services/SimpleMarkdown.cs: headings, fenced code, lists, paragraphs,
// and inline `code`, **bold** and [links](https://…). Output is built from
// React elements, so model text can never inject HTML.

import { Fragment, type ReactNode } from 'react';

type Block =
  | { kind: 'heading'; level: number; text: string }
  | { kind: 'code'; lang: string; code: string }
  | { kind: 'list'; ordered: boolean; items: string[] }
  | { kind: 'paragraph'; text: string };

export function parseMarkdown(markdown: string): Block[] {
  const blocks: Block[] = [];
  const lines = (markdown ?? '').replace(/\r\n/g, '\n').split('\n');
  let paragraph: string[] = [];
  let list: string[] = [];
  let ordered = false;
  let code: string[] = [];
  let lang = '';
  let inCode = false;

  const flushParagraph = () => {
    if (paragraph.length) blocks.push({ kind: 'paragraph', text: paragraph.join('\n') });
    paragraph = [];
  };
  const flushList = () => {
    if (list.length) blocks.push({ kind: 'list', ordered, items: list });
    list = [];
  };

  for (const line of lines) {
    if (line.startsWith('```')) {
      if (inCode) {
        blocks.push({ kind: 'code', lang, code: code.join('\n') });
        code = [];
        lang = '';
        inCode = false;
      } else {
        flushParagraph();
        flushList();
        lang = line.slice(3).trim();
        inCode = true;
      }
      continue;
    }
    if (inCode) {
      code.push(line);
      continue;
    }
    const heading = /^(#{1,4})\s+(.*)$/.exec(line);
    if (heading) {
      flushParagraph();
      flushList();
      blocks.push({ kind: 'heading', level: heading[1].length, text: heading[2] });
      continue;
    }
    const un = /^[-*]\s+(.*)$/.exec(line);
    const ord = /^(\d+)[.)]\s+(.*)$/.exec(line);
    if (un || ord) {
      flushParagraph();
      const isOrdered = !!ord;
      if (list.length && ordered !== isOrdered) flushList();
      ordered = isOrdered;
      list.push(ord ? ord[2] : un![1]);
      continue;
    }
    if (!line.trim()) {
      flushParagraph();
      flushList();
      continue;
    }
    paragraph.push(line.trim());
  }
  if (inCode) blocks.push({ kind: 'code', lang, code: code.join('\n') });
  flushParagraph();
  flushList();
  return blocks;
}

/** Inline formatting: `code`, **bold**, [label](https://url), line breaks. */
export function renderInline(text: string): ReactNode[] {
  const out: ReactNode[] = [];
  const re = /`([^`\n]+)`|\*\*([^*]+)\*\*|\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)|\n/g;
  let last = 0;
  let m: RegExpExecArray | null;
  let key = 0;
  while ((m = re.exec(text))) {
    if (m.index > last) out.push(text.slice(last, m.index));
    if (m[1] !== undefined) out.push(<code key={key++}>{m[1]}</code>);
    else if (m[2] !== undefined) out.push(<strong key={key++}>{m[2]}</strong>);
    else if (m[3] !== undefined)
      out.push(
        <a key={key++} href={m[4]} target="_blank" rel="noopener noreferrer">
          {m[3]}
        </a>,
      );
    else out.push(<br key={key++} />);
    last = re.lastIndex;
  }
  if (last < text.length) out.push(text.slice(last));
  return out;
}

export function Markdown({ text, onCopy }: { text: string; onCopy?: (code: string) => void }) {
  return (
    <>
      {parseMarkdown(text).map((b, i) => {
        switch (b.kind) {
          case 'heading': {
            const Tag = (`h${Math.min(6, b.level + 2)}` as unknown) as 'h3';
            return (
              <Tag key={i} dir="auto">
                {renderInline(b.text)}
              </Tag>
            );
          }
          case 'code':
            return (
              <div className="md-code" key={i}>
                <div className="md-code-head">
                  <span>{b.lang || 'code'}</span>
                  {onCopy && (
                    <button type="button" className="btn btn-sm btn-secondary" onClick={() => onCopy(b.code)}>
                      Copy
                    </button>
                  )}
                </div>
                <pre dir="auto">{b.code}</pre>
              </div>
            );
          case 'list': {
            const items = b.items.map((it, j) => (
              <li key={j} dir="auto">
                {renderInline(it)}
              </li>
            ));
            return b.ordered ? <ol key={i}>{items}</ol> : <ul key={i}>{items}</ul>;
          }
          default:
            return (
              <p key={i} dir="auto">
                {renderInline(b.text)}
              </p>
            );
        }
      })}
    </>
  );
}

export { Fragment };
