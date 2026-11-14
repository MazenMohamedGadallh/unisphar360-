import { useState, type FormEvent } from 'react';
import { errorMessage } from '../../lib/api';
import { homeFor, useAuth } from '../../lib/auth';
import { Link, useNavigate, useSearchParams } from '../../lib/router';
import { usePageTitle } from '../../lib/hooks';

const DEMO_ACCOUNTS = [
  { role: 'Student', email: 'mazenmohamed@unisphere.edu', password: 'Password123!' },
  { role: 'Student (Helwan)', email: 'helwan.student@unisphere.edu', password: 'Password123!' },
  { role: 'Graduate', email: 'graduate@unisphere.edu', password: 'Graduate123!' },
  { role: 'Doctor', email: 'doctor@unisphere.edu', password: 'Doctor123!' },
  { role: 'Admin', email: 'admin@unisphere.edu', password: 'Admin123!' },
];

/** Only allow local app paths as a post-login destination (no open redirects). */
export function isSafeReturnUrl(u: string | null): u is string {
  return (
    !!u &&
    u.startsWith('/') &&
    !u.startsWith('//') &&
    !u.startsWith('/\\') &&
    !u.includes('\\') &&
    // eslint-disable-next-line no-control-regex
    !/[\u0000-\u001f]/.test(u) &&
    u !== '/login'
  );
}

export default function Login() {
  usePageTitle('Login');
  const { login, expired } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError(null);
    try {
      const user = await login(email, password);
      const returnUrl = params.get('returnUrl');
      navigate(isSafeReturnUrl(returnUrl) ? returnUrl : homeFor(user.role), { replace: true });
    } catch (err) {
      setError(errorMessage(err, 'Login failed. Please try again.'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-container">
      <div className="login-card">
        <div className="login-header">
          <h1>Welcome Back</h1>
          <p>Sign in to your UniSphere 360+ account</p>
        </div>

        {(expired || params.get('expired') === '1') && !error && (
          <div className="alert alert-error">Session expired. Please sign in again.</div>
        )}
        {error && <div className="alert alert-error">{error}</div>}

        <form onSubmit={submit}>
          <div className="form-group">
            <label htmlFor="email">Email</label>
            <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="Enter your email" autoComplete="username" required />
          </div>
          <div className="form-group">
            <label htmlFor="password">Password</label>
            <input
              id="password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Enter your password"
              autoComplete="current-password"
              required
            />
          </div>
          <button type="submit" className="btn btn-primary" disabled={loading}>
            {loading ? 'Signing in…' : 'Sign In'}
          </button>
        </form>

        <div className="auth-switch">
          <span>
            Don&apos;t have an account? <Link to="/register">Register</Link>
          </span>
        </div>

        <div className="login-footer">
          <p>Demo accounts (click to fill, then Sign In):</p>
          <ul className="demo-accounts">
            {DEMO_ACCOUNTS.map((a, aIdx) => (
              <li key={`${a.email}-${aIdx}`}>
                <button
                  type="button"
                  className="demo-btn"
                  onClick={() => {
                    setEmail(a.email);
                    setPassword(a.password);
                    setError(null);
                  }}
                >
                  <strong>{a.role}</strong>
                  <span>{a.email}</span>
                  <span>{a.password}</span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
}
