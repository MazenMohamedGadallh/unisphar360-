import { useState } from "react";

const UNIVERSITY_CAMPUS_HERO = "/src/assets/images/university_campus_hero_1795788262420.jpg";
const UNIVERSITY_LIBRARY_BACKDROP = "/src/assets/images/university_library_backdrop_1795788262886.jpg";

interface LiveCampusBackgroundProps {
  variant?: "workspace" | "hero";
  portalActive?: boolean;
}

/**
 * Prestigious university campus & architectural library atmosphere for both
 * the full-bleed Login hero and the authenticated workspace shell.
 * Strictly GPU-composited (transform + opacity) and prefers-reduced-motion safe.
 */
export function LiveCampusBackground({
  variant = "workspace",
  portalActive = false,
}: LiveCampusBackgroundProps) {
  const [heroPhotoFailed, setHeroPhotoFailed] = useState(false);
  const [workspacePhotoFailed, setWorkspacePhotoFailed] = useState(false);

  if (variant === "hero") {
    return (
      <div
        className="pointer-events-none fixed inset-0 z-0 overflow-hidden bg-[#160d2e]"
        aria-hidden="true"
      >
        {/* Full-bleed University Campus Quad Photograph */}
        {!heroPhotoFailed && (
          <img
            src={UNIVERSITY_CAMPUS_HERO}
            alt=""
            referrerPolicy="no-referrer"
            onError={() => setHeroPhotoFailed(true)}
            className="live-campus-photo absolute inset-0 h-full w-full object-cover object-center"
            style={
              portalActive
                ? {
                    animation: "none",
                    transform: "scale(1.06)",
                    transition: "transform 580ms cubic-bezier(0.16, 1, 0.3, 1)",
                  }
                : undefined
            }
          />
        )}

        {/* Measured Royal University Purple architectural contrast scrim — preserves campus quad clarity with rich purple atmosphere */}
        <div
          className="absolute inset-0"
          style={{
            background:
              "linear-gradient(115deg, rgba(28, 14, 62, 0.72) 0%, rgba(49, 22, 98, 0.44) 48%, rgba(24, 11, 54, 0.72) 100%), linear-gradient(180deg, rgba(24, 11, 54, 0.44) 0%, rgba(45, 20, 92, 0.14) 40%, rgba(20, 10, 44, 0.58) 100%)",
          }}
        />

        {/* Subtle luminous royal violet ambient bloom over the campus facade */}
        <div
          className="absolute -top-24 left-[26%] h-[420px] w-[560px] rounded-full opacity-40 blur-3xl"
          style={{
            background:
              "radial-gradient(circle, rgba(167, 139, 250, 0.32) 0%, rgba(139, 92, 246, 0) 70%)",
          }}
        />
      </div>
    );
  }

  // Workspace variant: serene architectural university library & limestone drafting backdrop
  return (
    <div
      className="live-workspace-bg pointer-events-none fixed inset-0 z-0 overflow-hidden"
      aria-hidden="true"
    >
      {/* Subtle architectural university library atrium photograph */}
      {!workspacePhotoFailed && (
        <img
          src={UNIVERSITY_LIBRARY_BACKDROP}
          alt=""
          referrerPolicy="no-referrer"
          onError={() => setWorkspacePhotoFailed(true)}
          className="live-workspace-photo absolute inset-0 h-full w-full object-cover object-center"
        />
      )}

      {/* Soft Academic Daylight / Oxford Slate Atmospheric Wash */}
      <div className="live-workspace-veil absolute inset-0" />
      <div className="live-orb-1 live-workspace-orb-a absolute -top-40 left-[6%] h-[540px] w-[540px] rounded-full blur-3xl" />
      <div className="live-orb-2 live-workspace-orb-b absolute top-[28%] -right-32 h-[500px] w-[500px] rounded-full blur-3xl" />

      {/* Dignified University Architectural Engraving & Blueprint Linework */}
      <svg
        className="live-academic-svg absolute inset-0 h-full w-full"
        xmlns="http://www.w3.org/2000/svg"
        preserveAspectRatio="xMidYMid slice"
        viewBox="0 0 1440 900"
      >
        <defs>
          <pattern id="campus-architectural-grid" width="48" height="48" patternUnits="userSpaceOnUse">
            <path
              d="M 48 0 L 0 0 0 48"
              fill="none"
              stroke="currentColor"
              strokeWidth="0.6"
              opacity="0.14"
            />
          </pattern>
        </defs>

        {/* Fine architectural drafting grid */}
        <rect width="1440" height="900" fill="url(#campus-architectural-grid)" opacity="0.55" />

        {/* Classical University Facade, Pediment & Colonnade (bottom-right anchor) */}
        <g stroke="currentColor" fill="none" strokeWidth="1.15" opacity="0.13">
          {/* Pediment roof */}
          <path d="M935 612 L1170 502 L1405 612 Z" />
          <path d="M975 600 L1170 518 L1365 600" strokeDasharray="3 5" />
          <circle cx="1170" cy="566" r="18" />
          {/* Entablature */}
          <rect x="945" y="612" width="450" height="24" />
          {/* Classical ionic columns */}
          <rect x="975" y="636" width="26" height="224" />
          <rect x="1062" y="636" width="26" height="224" />
          <rect x="1157" y="636" width="26" height="224" />
          <rect x="1252" y="636" width="26" height="224" />
          <rect x="1339" y="636" width="26" height="224" />
          {/* Arched library windows between columns */}
          <path d="M1014 860 V728 A18 18 0 0 1 1050 728 V860" opacity="0.7" />
          <path d="M1102 860 V716 A21 21 0 0 1 1144 716 V860" opacity="0.7" />
          <path d="M1196 860 V716 A21 21 0 0 1 1238 716 V860" opacity="0.7" />
          <path d="M1290 860 V728 A18 18 0 0 1 1326 728 V860" opacity="0.7" />
          {/* Grand marble steps */}
          <line x1="910" y1="860" x2="1430" y2="860" />
          <line x1="892" y1="876" x2="1440" y2="876" />
        </g>

        {/* Classical Academic Compass & Celestial Arcs (top-right subtle motif) */}
        <g className="live-drift-slow" stroke="currentColor" fill="none" strokeWidth="1" opacity="0.11">
          <circle cx="1275" cy="145" r="132" strokeDasharray="5 8" />
          <circle cx="1275" cy="145" r="92" />
          <line x1="1275" y1="5" x2="1275" y2="285" strokeDasharray="3 7" />
          <line x1="1135" y1="145" x2="1415" y2="145" strokeDasharray="3 7" />
          <path d="M1275 88 L1287 133 L1332 145 L1287 157 L1275 202 L1263 157 L1218 145 L1263 133 Z" />
        </g>

        {/* Subtle Academic Crest & Open Volume Linework (lower-left corner) */}
        <g stroke="currentColor" fill="none" strokeWidth="1.15" opacity="0.10" transform="translate(88 688)">
          <path d="M12 24 C34 15 56 18 70 30 C84 18 106 15 128 24 V96 C106 87 84 90 70 102 C56 90 34 87 12 96 Z" />
          <line x1="70" y1="30" x2="70" y2="102" />
          <line x1="26" y1="44" x2="56" y2="48" />
          <line x1="26" y1="60" x2="56" y2="64" />
          <line x1="84" y1="48" x2="114" y2="44" />
          <line x1="84" y1="64" x2="114" y2="60" />
        </g>
      </svg>
    </div>
  );
}
