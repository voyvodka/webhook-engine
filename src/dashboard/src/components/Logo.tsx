interface LogoProps {
  /** Pixel size of the square logo. Defaults to 30 to match the landing page. */
  size?: number;
  className?: string;
  /** Render only the icon, no surrounding rounded background. */
  bare?: boolean;
}

/**
 * The WebhookEngine mark — a fan-out from a diamond into three accent dots,
 * shared with the landing page so the brand reads consistently across surfaces.
 */
export function Logo({ size = 30, className, bare = false }: LogoProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 30 30"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      role="img"
      aria-label="WebhookEngine"
      className={className}
    >
      {!bare && (
        <rect x="1" y="1" width="28" height="28" rx="7" fill="#0C1018" stroke="#1C2535" />
      )}
      <path
        d="M8 15 L13 10 L18 15 L13 20 Z"
        fill="none"
        stroke="#3EFFA0"
        strokeWidth="1.5"
        strokeLinejoin="round"
      />
      <circle cx="21" cy="10" r="2" fill="#3EFFA0" opacity="0.6" />
      <circle cx="21" cy="15" r="2" fill="#3EFFA0" />
      <circle cx="21" cy="20" r="2" fill="#3EFFA0" opacity="0.4" />
      <line x1="18" y1="15" x2="19" y2="15" stroke="#3EFFA0" strokeWidth="1.5" />
      <line x1="18" y1="10.5" x2="19" y2="10.2" stroke="#3EFFA0" strokeWidth="1" opacity="0.5" />
      <line x1="18" y1="19.5" x2="19" y2="19.8" stroke="#3EFFA0" strokeWidth="1" opacity="0.3" />
    </svg>
  );
}

interface LogoWordmarkProps {
  size?: number;
  className?: string;
}

/**
 * The full lockup: icon + "WebhookEngine" wordmark with the accent split
 * (Webhook in the body color, Engine in the accent color), matching the
 * landing-page header.
 */
export function LogoWordmark({ size = 24, className }: LogoWordmarkProps) {
  return (
    <span className={`inline-flex items-center gap-2 ${className ?? ""}`}>
      <Logo size={size} />
      <span className="text-sm font-semibold tracking-tight">
        Webhook<span className="text-accent">Engine</span>
      </span>
    </span>
  );
}
