import { createElement } from "react";
import { copyToClipboard } from "./clipboard";

/*
 * Faithful port of the backend's Services/SimpleMarkdown.cs - the same
 * minimal Markdown subset, rendered WITHOUT raw HTML passthrough: every
 * text fragment is HTML-encoded first, so model output can never inject
 * markup or scripts. Supports fenced code blocks, headings, lists, bold,
 * inline code and http(s) links. Block texts render with dir="auto" for
 * Arabic RTL support. No external dependencies.
 */

export type MdBlock =
  | { kind: "heading"; level: number; html: string }
  | { kind: "code"; language: string; code: string }
  | { kind: "list"; items: string[]; ordered: boolean }
  | { kind: "paragraph"; html: string };

/** Equivalent of System.Net.WebUtility.HtmlEncode for the characters
 *  that matter (markup + quotes). Encode "&" first so already-encoded
 *  entities are not double-processed. */
function htmlEncode(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

/** Inline formatting on already-safe HTML: bold, code, links.
 *  Mirrors SimpleMarkdown.Inline step by step. */
function inline(text: string): string {
  // 1. Pull out `code` spans so inner markup is never interpreted.
  const codes: string[] = [];
  const staged = text.replace(/`([^`\n]+)`/g, (_match, code: string) => {
    codes.push(htmlEncode(code));
    return "\0" + (codes.length - 1) + "\0";
  });

  // 2. Encode everything else.
  let html = htmlEncode(staged);

  // 3. Links [text](http...) - http(s) only.
  html = html.replace(
    /\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g,
    (_match, label: string, url: string) =>
      `<a href="${url}" target="_blank" rel="noopener">${label}</a>`,
  );

  // 4. Bold **text**.
  html = html.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");

  // 5. Restore code spans.
  html = html.replace(/\0(\d+)\0/g, (_match, index: string) => {
    const code = codes[Number(index)];
    return code === undefined ? "" : `<code>${code}</code>`;
  });

  return html.replace(/\n/g, "<br />");
}

/** Block-level parser - mirrors SimpleMarkdown.Parse. */
function parseBlocks(markdown: string): MdBlock[] {
  const blocks: MdBlock[] = [];
  const lines = (markdown ?? "").replace(/\r\n/g, "\n").split("\n");
  let paragraph: string[] = [];
  let listItems: string[] = [];
  let listOrdered = false;
  let codeLines: string[] = [];
  let codeLang = "";
  let inCode = false;

  const flushParagraph = () => {
    if (paragraph.length > 0) {
      blocks.push({ kind: "paragraph", html: inline(paragraph.join("\n")) });
      paragraph = [];
    }
  };

  const flushList = () => {
    if (listItems.length > 0) {
      blocks.push({ kind: "list", items: [...listItems], ordered: listOrdered });
      listItems = [];
    }
  };

  for (const line of lines) {
    if (line.startsWith("```")) {
      if (inCode) {
        blocks.push({
          kind: "code",
          language: codeLang,
          code: codeLines.join("\n").replace(/\n+$/, ""),
        });
        codeLines = [];
        codeLang = "";
        inCode = false;
      } else {
        flushParagraph();
        flushList();
        codeLang = line.length > 3 ? line.slice(3).trim() : "";
        inCode = true;
      }
      continue;
    }
    if (inCode) {
      codeLines.push(line);
      continue;
    }

    const heading = /^(#{1,4})\s+(.*)$/.exec(line);
    if (heading) {
      flushParagraph();
      flushList();
      blocks.push({
        kind: "heading",
        level: heading[1].length,
        html: inline(heading[2]),
      });
      continue;
    }

    const unordered = /^[-*]\s+(.*)$/.exec(line);
    const ordered = /^(\d+)[.)]\s+(.*)$/.exec(line);
    if (unordered || ordered) {
      flushParagraph();
      const isOrdered = ordered !== null;
      if (listItems.length > 0 && listOrdered !== isOrdered) flushList();
      listOrdered = isOrdered;
      listItems.push(
        inline(isOrdered ? (ordered as RegExpExecArray)[2] : (unordered as RegExpExecArray)[1]),
      );
      continue;
    }

    if (line.trim() === "") {
      flushParagraph();
      flushList();
      continue;
    }

    paragraph.push(line.trim());
  }

  if (inCode) {
    blocks.push({
      kind: "code",
      language: codeLang,
      code: codeLines.join("\n").replace(/\n+$/, ""),
    });
  }
  flushParagraph();
  flushList();
  return blocks;
}

function renderBlock(block: MdBlock, key: number) {
  switch (block.kind) {
    case "heading": {
      const html = { __html: block.html };
      if (block.level <= 1) return createElement("h1", { key, dir: "auto", dangerouslySetInnerHTML: html });
      if (block.level === 2) return createElement("h2", { key, dir: "auto", dangerouslySetInnerHTML: html });
      if (block.level === 3) return createElement("h3", { key, dir: "auto", dangerouslySetInnerHTML: html });
      return createElement("h4", { key, dir: "auto", dangerouslySetInnerHTML: html });
    }
    case "code":
      return (
        <div className="md-code" key={key}>
          <div className="md-code-head">
            <span>{block.language || "code"}</span>
            <button
              type="button"
              className="btn btn-sm btn-secondary"
              onClick={() => copyToClipboard(block.code)}
            >
              Copy
            </button>
          </div>
          <pre dir="auto">{block.code}</pre>
        </div>
      );
    case "list": {
      const items = block.items.map((item, index) => (
        <li key={index} dir="auto" dangerouslySetInnerHTML={{ __html: item }} />
      ));
      return block.ordered ? (
        <ol key={key}>{items}</ol>
      ) : (
        <ul key={key}>{items}</ul>
      );
    }
    case "paragraph":
      return <p key={key} dir="auto" dangerouslySetInnerHTML={{ __html: block.html }} />;
  }
}

/** Renders a Markdown string as React elements (same output as the
 *  Blazor SimpleMarkdown renderer). */
export function Markdown({ text }: { text: string }) {
  return <>{parseBlocks(text).map((block, index) => renderBlock(block, index))}</>;
}
