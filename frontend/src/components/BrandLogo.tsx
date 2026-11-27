import { useState } from "react";

const LOGO_SRC = "/src/assets/images/unisphere_sphere_logo_1795776222557.jpg";

interface BrandLogoProps {
  size?: number;
  className?: string;
}

/**
 * Clean circular glowing cyan-and-periwinkle swirling sphere "+" logo
 * with inline SVG fallback (Zero-Broken-Image guarantee).
 */
export function BrandLogo({ size = 34, className = "" }: BrandLogoProps) {
  const [failed, setFailed] = useState(false);

  return (
    <span
      className={`inline-flex shrink-0 items-center justify-center overflow-hidden rounded-full ring-1 ring-primary/30 shadow-sm ${className}`}
      style={{ width: size, height: size, background: "#0B0D1E" }}
    >
      {!failed ? (
        <img
          src={LOGO_SRC}
          alt="UniSphere 360+ logo"
          referrerPolicy="no-referrer"
          onError={() => setFailed(true)}
          className="h-full w-full object-cover"
          style={{ transform: "scale(1.08)" }}
        />
      ) : (
        <svg viewBox="0 0 40 40" className="h-full w-full" aria-hidden="true">
          <defs>
            <linearGradient id="us-sphere-grad" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0%" stopColor="#38BDF8" />
              <stop offset="100%" stopColor="#818CF8" />
            </linearGradient>
          </defs>
          <circle cx="20" cy="20" r="19" fill="#0B0D1E" />
          <circle cx="20" cy="20" r="11" fill="none" stroke="url(#us-sphere-grad)" strokeWidth="2.4" />
          <ellipse cx="20" cy="20" rx="11" ry="5" transform="rotate(-35 20 20)" fill="none" stroke="#38BDF8" strokeWidth="1.8" />
          <ellipse cx="20" cy="20" rx="11" ry="5" transform="rotate(35 20 20)" fill="none" stroke="#818CF8" strokeWidth="1.8" />
          <path d="M28 9v6M25 12h6" stroke="#38BDF8" strokeWidth="2.2" strokeLinecap="round" />
        </svg>
      )}
    </span>
  );
}
