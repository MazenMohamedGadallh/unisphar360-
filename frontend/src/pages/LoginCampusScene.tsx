/**
 * Decorative campus scene for the Login brand panel — flat vector,
 * tone-on-tone UniSphere blues (#2563EB family) with teal (#14B8A6)
 * accents. Rendered inline (no network request) and bottom-anchored so
 * the buildings stay visible at any panel aspect ratio; the CSS veil
 * (.login-brand-scrim) keeps the overlay text readable.
 *
 * Static by construction: every animation target (.login-scene-svg,
 * .cloud-a/.cloud-b) freezes under prefers-reduced-motion, which makes
 * this SVG the required static poster/fallback. Clouds are animated
 * from login.css; everything here is pure geometry.
 */
export function LoginCampusScene() {
  return (
    <svg
      className="login-scene-svg"
      viewBox="0 0 760 1040"
      preserveAspectRatio="xMidYMax slice"
      aria-hidden="true"
      focusable="false"
    >
      {/* Rising sun */}
      <circle cx="552" cy="232" r="128" fill="#3B82F6" opacity="0.5" />
      <circle cx="552" cy="232" r="90" fill="#60A5FA" opacity="0.55" />

      {/* Clouds (drifted by CSS) */}
      <g className="cloud-a" fill="#93C5FD" opacity="0.5">
        <rect x="110" y="150" width="150" height="26" rx="13" />
        <rect x="150" y="122" width="100" height="24" rx="12" />
      </g>
      <g className="cloud-b" fill="#93C5FD" opacity="0.4">
        <rect x="470" y="96" width="130" height="24" rx="12" />
        <rect x="505" y="72" width="86" height="22" rx="11" />
      </g>

      {/* Distant hills */}
      <path
        d="M0 706 Q 190 640 380 682 T 760 664 L 760 1040 L 0 1040 Z"
        fill="#3B82F6"
        opacity="0.35"
      />

      {/* Ground */}
      <path d="M0 768 Q 380 706 760 768 L 760 1040 L 0 1040 Z" fill="#1E3A8A" />

      {/* Walkway to the library (teal accent dashes) */}
      <path
        d="M300 1040 C 316 952 342 892 380 830"
        fill="none"
        stroke="#93C5FD"
        strokeWidth="14"
        strokeLinecap="round"
        opacity="0.45"
      />
      <path
        d="M300 1040 C 316 952 342 892 380 830"
        fill="none"
        stroke="#14B8A6"
        strokeWidth="4"
        strokeLinecap="round"
        strokeDasharray="1 22"
        opacity="0.85"
      />

      {/* Left faculty building */}
      <g>
        <rect x="70" y="640" width="150" height="180" fill="#DBEAFE" />
        <rect x="70" y="640" width="150" height="14" fill="#BFDBFE" />
        <rect x="94" y="672" width="26" height="34" fill="#2563EB" />
        <rect x="166" y="672" width="26" height="34" fill="#14B8A6" />
        <rect x="94" y="726" width="26" height="34" fill="#2563EB" />
        <rect x="166" y="726" width="26" height="34" fill="#2563EB" />
      </g>

      {/* Right faculty building */}
      <g>
        <rect x="540" y="655" width="150" height="165" fill="#BFDBFE" />
        <rect x="540" y="655" width="150" height="14" fill="#DBEAFE" />
        <rect x="564" y="688" width="26" height="32" fill="#2563EB" />
        <rect x="636" y="688" width="26" height="32" fill="#14B8A6" />
        <rect x="564" y="738" width="26" height="32" fill="#2563EB" />
        <rect x="636" y="738" width="26" height="32" fill="#2563EB" />
      </g>

      {/* Main library with clock tower */}
      <g>
        <rect x="358" y="418" width="44" height="92" fill="#DBEAFE" />
        <path d="M352 418 L 380 392 L 408 418 Z" fill="#93C5FD" />
        <circle cx="380" cy="446" r="14" fill="#FFFFFF" />
        <path
          d="M380 446 L 380 437 M380 446 L 386 449"
          stroke="#2563EB"
          strokeWidth="2.5"
          strokeLinecap="round"
          fill="none"
        />
        <rect x="255" y="565" width="250" height="255" fill="#EFF6FF" />
        <rect x="255" y="565" width="250" height="18" fill="#DBEAFE" />
        <path d="M235 565 L 380 490 L 525 565 Z" fill="#BFDBFE" />
        {/* Arched windows (middle one is the teal accent) */}
        <path d="M286 662 v-37 a17 17 0 0 1 34 0 v37 z" fill="#2563EB" />
        <path d="M371 662 v-37 a17 17 0 0 1 34 0 v37 z" fill="#14B8A6" />
        <path d="M456 662 v-37 a17 17 0 0 1 34 0 v37 z" fill="#2563EB" />
        <path d="M286 760 v-37 a17 17 0 0 1 34 0 v37 z" fill="#2563EB" />
        <path d="M456 760 v-37 a17 17 0 0 1 34 0 v37 z" fill="#2563EB" />
        {/* Arched door */}
        <path d="M361 820 v-101 a19 19 0 0 1 38 0 v101 z" fill="#1E3A8A" />
      </g>

      {/* Trees (teal + one light-blue for variety) */}
      <g>
        <rect x="46" y="806" width="12" height="30" fill="#1E3A8A" />
        <circle cx="52" cy="786" r="34" fill="#14B8A6" />
      </g>
      <g>
        <rect x="233" y="828" width="10" height="24" fill="#1E3A8A" />
        <circle cx="238" cy="812" r="26" fill="#0D9488" />
      </g>
      <g>
        <rect x="718" y="812" width="12" height="28" fill="#1E3A8A" />
        <circle cx="724" cy="792" r="34" fill="#14B8A6" />
      </g>
      <g>
        <rect x="512" y="850" width="8" height="20" fill="#1E3A8A" />
        <circle cx="516" cy="836" r="22" fill="#93C5FD" />
      </g>

      {/* Foreground bushes anchoring the bottom edge */}
      <ellipse cx="100" cy="1048" rx="150" ry="44" fill="#172554" />
      <ellipse cx="660" cy="1050" rx="170" ry="48" fill="#172554" />
      <ellipse cx="380" cy="1056" rx="240" ry="40" fill="#172554" />
    </svg>
  );
}
