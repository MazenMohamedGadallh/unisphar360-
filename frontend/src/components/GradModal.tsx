import { useEffect, useRef, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { UiIcon } from "../UiIcon";

/** Dialog shell for the graduate flows; `drawer` slides it to the right edge. */
export function GradModal({
  title,
  drawer = false,
  onClose,
  footer,
  children,
}: {
  title: string;
  drawer?: boolean;
  onClose: () => void;
  /** Always visible under the scrolling body. */
  footer?: ReactNode;
  children: ReactNode;
}) {
  const panel = useRef<HTMLDivElement>(null);
  useEffect(() => panel.current?.focus(), []);

  return createPortal(
    <div
      className={`fixed inset-0 z-[900] flex bg-black/50 ${drawer ? "justify-end" : "items-center justify-center p-4"}`}
    >
      <div
        ref={panel}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onKeyDown={(e) => {
          if (e.key === "Escape") onClose();
        }}
        className={`flex w-full flex-col overflow-hidden border border-border bg-surface shadow-card-lg outline-none ${
          drawer ? "h-full max-w-xl" : "max-h-[85vh] max-w-2xl rounded-card"
        }`}
      >
        <div className="flex items-center justify-between gap-3 border-b border-border px-5 py-4">
          <h2 className="m-0 text-base font-semibold text-text">{title}</h2>
          <button
            type="button"
            onClick={onClose}
            className="inline-flex shrink-0 cursor-pointer items-center gap-1.5 rounded-md border border-border bg-bg p-1.5 text-text-secondary hover:text-text"
          >
            <UiIcon name="x" />
            Close
          </button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto p-5 text-sm">{children}</div>
        {footer && <div className="border-t border-border px-5 py-3 text-sm">{footer}</div>}
      </div>
    </div>,
    document.body,
  );
}

export const gradInput =
  "w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary";
