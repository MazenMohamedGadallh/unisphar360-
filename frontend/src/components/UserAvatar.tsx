import { useState } from "react";
import { assetUrl } from "../lib/api";

interface UserAvatarProps {
  fullName?: string | null;
  avatarUrl?: string | null;
  size?: number;
  className?: string;
}

/**
 * Circular user avatar (border-radius: 50%, object-fit: cover).
 * Falls back to a cool blue-lavender gradient circle with the user's first initial.
 */
export function UserAvatar({ fullName, avatarUrl, size = 36, className = "" }: UserAvatarProps) {
  const [imgFailed, setImgFailed] = useState(false);
  const initial = (fullName?.trim()?.[0] ?? "?").toUpperCase();
  const showPhoto = Boolean(avatarUrl && !imgFailed);

  return (
    <span
      className={`inline-flex shrink-0 items-center justify-center overflow-hidden rounded-full font-semibold text-white select-none ring-2 ring-primary/25 ${className}`}
      style={{
        width: size,
        height: size,
        fontSize: Math.max(12, Math.round(size * 0.42)),
        background: "linear-gradient(135deg, #6366F1 0%, #38BDF8 100%)",
      }}
      aria-hidden="true"
    >
      {showPhoto ? (
        <img
          src={assetUrl(avatarUrl!)}
          alt={fullName ? `${fullName} avatar` : "User avatar"}
          referrerPolicy="no-referrer"
          onError={() => setImgFailed(true)}
          className="h-full w-full rounded-full object-cover"
        />
      ) : (
        initial
      )}
    </span>
  );
}
