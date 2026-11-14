// A small client-side router built on the History API.
//
// The app only needs a handful of routing features (path params, query
// strings, links, programmatic navigation, a "replace" mode), so this keeps
// the project free of extra dependencies. Paths look like "/doctor/courses/:courseId".

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type AnchorHTMLAttributes,
  type MouseEvent,
  type ReactNode,
} from 'react';

interface Location {
  pathname: string;
  search: string;
}

interface RouterValue {
  location: Location;
  navigate: (to: string, opts?: { replace?: boolean }) => void;
}

const RouterContext = createContext<RouterValue | null>(null);
const ParamsContext = createContext<Record<string, string>>({});

function currentLocation(): Location {
  return { pathname: window.location.pathname, search: window.location.search };
}

export function RouterProvider({ children }: { children: ReactNode }) {
  const [location, setLocation] = useState<Location>(currentLocation);

  useEffect(() => {
    const onPop = () => setLocation(currentLocation());
    window.addEventListener('popstate', onPop);
    return () => window.removeEventListener('popstate', onPop);
  }, []);

  const navigate = useCallback((to: string, opts?: { replace?: boolean }) => {
    const target = new URL(to, window.location.origin);
    const next = target.pathname + target.search + target.hash;
    const now = window.location.pathname + window.location.search + window.location.hash;
    if (next !== now) {
      if (opts?.replace) window.history.replaceState(null, '', next);
      else window.history.pushState(null, '', next);
    }
    setLocation(currentLocation());
    if (!opts?.replace && !target.hash) window.scrollTo(0, 0);
  }, []);

  const value = useMemo(() => ({ location, navigate }), [location, navigate]);
  return <RouterContext.Provider value={value}>{children}</RouterContext.Provider>;
}

function useRouter(): RouterValue {
  const ctx = useContext(RouterContext);
  if (!ctx) throw new Error('Router hooks must be used inside <RouterProvider>.');
  return ctx;
}

export function useLocation(): Location {
  return useRouter().location;
}

export function useNavigate(): RouterValue['navigate'] {
  return useRouter().navigate;
}

export function useParams<T extends Record<string, string> = Record<string, string>>(): T {
  return useContext(ParamsContext) as T;
}

export function useSearchParams(): [URLSearchParams, (next: Record<string, string | null | undefined>) => void] {
  const { location, navigate } = useRouter();
  const params = useMemo(() => new URLSearchParams(location.search), [location.search]);
  const update = useCallback(
    (next: Record<string, string | null | undefined>) => {
      const sp = new URLSearchParams(location.search);
      for (const [k, v] of Object.entries(next)) {
        if (v === null || v === undefined || v === '') sp.delete(k);
        else sp.set(k, v);
      }
      const q = sp.toString();
      navigate(location.pathname + (q ? `?${q}` : ''), { replace: true });
    },
    [location.pathname, location.search, navigate],
  );
  return [params, update];
}

/** Match "/doctor/courses/:courseId" against a pathname. Returns params or null. */
export function matchPath(pattern: string, pathname: string): Record<string, string> | null {
  const clean = (p: string) => p.replace(/\/+$/, '') || '/';
  const pParts = clean(pattern).split('/');
  const uParts = clean(pathname).split('/');
  if (pParts.length !== uParts.length) return null;
  const params: Record<string, string> = {};
  for (let i = 0; i < pParts.length; i++) {
    const p = pParts[i];
    const u = uParts[i];
    if (p.startsWith(':')) {
      if (!u) return null;
      params[p.slice(1)] = decodeURIComponent(u);
    } else if (p.toLowerCase() !== u.toLowerCase()) {
      return null;
    }
  }
  return params;
}

export interface RouteDef {
  path: string;
  element: ReactNode;
}

/** Renders the first route whose pattern matches the current path. */
export function Routes({ routes, fallback }: { routes: RouteDef[]; fallback: ReactNode }) {
  const { location } = useRouter();
  for (const r of routes) {
    const params = matchPath(r.path, location.pathname);
    if (params) {
      return (
        <ParamsContext.Provider key={r.path} value={params}>
          {r.element}
        </ParamsContext.Provider>
      );
    }
  }
  return <>{fallback}</>;
}

export function Navigate({ to, replace = true }: { to: string; replace?: boolean }) {
  const navigate = useNavigate();
  useEffect(() => {
    navigate(to, { replace });
  }, [navigate, to, replace]);
  return null;
}

type LinkProps = Omit<AnchorHTMLAttributes<HTMLAnchorElement>, 'href'> & {
  to: string;
  replace?: boolean;
};

function isModifiedClick(e: MouseEvent<HTMLAnchorElement>): boolean {
  return e.button !== 0 || e.metaKey || e.altKey || e.ctrlKey || e.shiftKey;
}

export function Link({ to, replace, onClick, target, ...rest }: LinkProps) {
  const navigate = useNavigate();
  return (
    <a
      {...rest}
      href={to}
      target={target}
      onClick={(e) => {
        onClick?.(e);
        if (e.defaultPrevented || isModifiedClick(e) || (target && target !== '_self')) return;
        if (/^(https?:)?\/\//i.test(to) || to.startsWith('mailto:')) return;
        e.preventDefault();
        navigate(to, { replace });
      }}
    />
  );
}

/** Link that adds "active" to its class when the current path matches. */
export function NavLink({ to, end, className, ...rest }: LinkProps & { end?: boolean }) {
  const { location } = useRouter();
  const path = location.pathname.replace(/\/+$/, '').toLowerCase() || '/';
  const target = to.split('?')[0].replace(/\/+$/, '').toLowerCase() || '/';
  const active = end ? path === target : path === target || path.startsWith(`${target}/`);
  return <Link {...rest} to={to} className={[className, active ? 'active' : ''].filter(Boolean).join(' ')} />;
}
