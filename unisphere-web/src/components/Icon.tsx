import { ICONS, ICON_ALIASES, ICON_LEGACY } from './iconData';

/** Same lookup order as UiIconPaths.Resolve: name → alias → legacy (emoji) → circle. */
export function resolveIcon(name: string | null | undefined): string {
  if (!name) return ICONS.circle;
  const key = name.trim().replace(/️|‍/g, '');
  const lower = key.toLowerCase();
  if (ICONS[lower]) return ICONS[lower];
  const alias = ICON_ALIASES[lower];
  if (alias && ICONS[alias]) return ICONS[alias];
  const legacy = ICON_LEGACY[key];
  if (legacy && ICONS[legacy]) return ICONS[legacy];
  return ICONS.circle;
}

interface IconProps {
  name: string | null | undefined;
  size?: number | string;
  className?: string;
  title?: string;
}

/**
 * Lucide outline icon. The SVG geometry is static data generated from the
 * backend's Components/UiIconPaths.cs, never user input, so injecting it is safe.
 */
export function Icon({ name, size = '1em', className, title }: IconProps) {
  return (
    <svg
      className={className ? `ui-icon ${className}` : 'ui-icon'}
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden={title ? undefined : true}
      role={title ? 'img' : undefined}
      focusable="false"
      dangerouslySetInnerHTML={{ __html: (title ? `<title>${title.replace(/</g, '&lt;')}</title>` : '') + resolveIcon(name) }}
    />
  );
}
