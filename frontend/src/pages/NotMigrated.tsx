import { useLocation } from "react-router-dom";
import { UiIcon } from "../UiIcon";
import { Button } from "../components/ui";
import { API_BASE_URL } from "../lib/api";

/*
 * Fallback for routes that haven't been migrated to React yet. The
 * migration is page-by-page, so sidebar links whose pages are still
 * Blazor-only land here instead of a blank screen — with a direct link
 * to the original page on the Blazor app.
 */
export function NotMigrated() {
  const location = useLocation();
  const originalUrl = `${API_BASE_URL}${location.pathname}`;
  return (
    <div className="flex flex-col items-center gap-4 px-6 py-20 text-center">
      <span className="flex h-14 w-14 items-center justify-center rounded-full bg-primary-light text-2xl text-primary">
        <UiIcon name="circle-help" />
      </span>
      <h1 className="text-xl font-semibold">This page isn&apos;t migrated yet</h1>
      <p className="max-w-[440px] text-sm text-text-secondary">
        The migration to the new React interface is still in progress, so this
        section is currently only available in the original Blazor app at{" "}
        <a className="text-primary underline" href={originalUrl}>
          {originalUrl}
        </a>
        .
      </p>
      <Button to="/" variant="secondary">
        Back to Home
      </Button>
    </div>
  );
}
