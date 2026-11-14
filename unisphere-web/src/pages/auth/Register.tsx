import { useEffect, useState, type FormEvent } from 'react';
import { ApiError, api, errorMessage } from '../../lib/api';
import { homeFor, useAuth } from '../../lib/auth';
import { Link, useNavigate } from '../../lib/router';
import { usePageTitle } from '../../lib/hooks';
import type { RegisterRequest, UniversityDto } from '../../types/dto';

type RegRole = 'Student' | 'Doctor' | 'Graduate';
const ROLES: RegRole[] = ['Student', 'Doctor', 'Graduate'];
const DEPARTMENTS = ['Computer Science', 'Information Systems', 'Artificial Intelligence', 'Engineering', 'Business', 'Medicine', 'Other'];
// Services/RegistrationRules.GetMinAge
const MIN_AGE: Record<RegRole, number> = { Student: 18, Doctor: 25, Graduate: 22 };

interface Form {
  role: RegRole;
  universityId: string;
  firstName: string;
  middleName: string;
  lastName: string;
  age: string;
  gender: string;
  academicYear: string;
  department: string;
  specialization: string;
  graduatedFrom: string;
  graduationYear: string;
  phoneNumber: string;
  email: string;
  address: string;
  password: string;
  confirmPassword: string;
}

const EMPTY: Form = {
  role: 'Student',
  universityId: '',
  firstName: '',
  middleName: '',
  lastName: '',
  age: '',
  gender: '',
  academicYear: '',
  department: '',
  specialization: '',
  graduatedFrom: '',
  graduationYear: '',
  phoneNumber: '',
  email: '',
  address: '',
  password: '',
  confirmPassword: '',
};

/** Client-side checks mirroring the backend RegisterRequest validator; the server stays the authority. */
function validate(f: Form): Record<string, string> {
  const e: Record<string, string> = {};
  if (!f.firstName.trim()) e.firstName = 'First name is required.';
  if (!f.lastName.trim()) e.lastName = 'Last name is required.';
  const age = Number(f.age);
  if (!f.age || !Number.isFinite(age)) e.age = 'Age is required.';
  else if (age < MIN_AGE[f.role]) e.age = `Minimum age for a ${f.role} is ${MIN_AGE[f.role]}.`;
  else if (age > 100) e.age = 'Please enter a valid age.';
  if (!f.gender) e.gender = 'Please select a gender.';
  // Same patterns as Validators/RequestValidators.cs (EgyptianPhone / InternationalPhone / PasswordPattern).
  const phone = f.phoneNumber.trim();
  if (!/^(\+20|0)?1[0125]\d{8}$/.test(phone) && !/^\+?[1-9]\d{7,14}$/.test(phone))
    e.phoneNumber = 'Enter a valid phone number (e.g. 01012345678 or +201012345678).';
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(f.email.trim())) e.email = 'Enter a valid email address.';
  if (!/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).{8,}$/.test(f.password))
    e.password = 'Password must be at least 8 characters and include an upper-case letter, a lower-case letter, a digit and a symbol.';
  if (!f.confirmPassword) e.confirmPassword = 'Please confirm your password.';
  else if (f.confirmPassword !== f.password) e.confirmPassword = 'Passwords do not match.';
  if (f.role === 'Student') {
    if (!f.academicYear) e.academicYear = 'Academic year is required for students.';
    if (!f.department) e.department = 'Department is required for students.';
  }
  if (f.role === 'Doctor' && !f.specialization.trim()) e.specialization = 'Specialization is required for doctors.';
  if (f.role === 'Graduate') {
    if (!f.graduatedFrom.trim()) e.graduatedFrom = 'Graduated from is required for graduates.';
    const y = Number(f.graduationYear);
    if (!f.graduationYear) e.graduationYear = 'Graduation year is required for graduates.';
    else if (y < 1950 || y > new Date().getFullYear()) e.graduationYear = `Graduation year must be between 1950 and ${new Date().getFullYear()}.`;
    if (!f.department) e.department = 'Department is required for graduates.';
  }
  return e;
}

export default function Register() {
  usePageTitle('Register');
  const { register } = useAuth();
  const navigate = useNavigate();
  const [form, setForm] = useState<Form>(EMPTY);
  const [universities, setUniversities] = useState<UniversityDto[]>([]);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    api
      .get<UniversityDto[]>('/api/auth/universities')
      .then((list) => {
        setUniversities(list ?? []);
        if (list && list.length > 0) setForm((f) => (f.universityId ? f : { ...f, universityId: String(list[0].id) }));
      })
      .catch(() => setUniversities([]));
  }, []);

  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm((f) => ({ ...f, [key]: value }));

  const selectRole = (role: RegRole) => {
    if (role === form.role) return;
    setForm((f) => ({ ...f, role, academicYear: '', specialization: '', graduatedFrom: '', graduationYear: '' }));
    setErrors({});
    setServerError(null);
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const v = validate(form);
    setErrors(v);
    setServerError(null);
    if (Object.keys(v).length > 0) return;

    const opt = (s: string) => (s.trim() ? s.trim() : null);
    const req: RegisterRequest = {
      firstName: form.firstName.trim(),
      middleName: opt(form.middleName),
      lastName: form.lastName.trim(),
      phoneNumber: form.phoneNumber.trim(),
      email: form.email.trim(),
      gender: form.gender,
      age: Number(form.age),
      role: form.role,
      password: form.password,
      confirmPassword: form.confirmPassword,
      academicYear: form.role === 'Student' && form.academicYear ? Number(form.academicYear) : null,
      department: opt(form.department),
      specialization: form.role === 'Doctor' ? opt(form.specialization) : null,
      graduatedFrom: form.role === 'Graduate' ? opt(form.graduatedFrom) : null,
      graduationYear: form.role === 'Graduate' && form.graduationYear ? Number(form.graduationYear) : null,
      universityId: form.universityId ? Number(form.universityId) : null,
      address: opt(form.address),
    };

    setLoading(true);
    try {
      const user = await register(req);
      navigate(homeFor(user.role), { replace: true });
    } catch (err) {
      if (err instanceof ApiError && Object.keys(err.fieldErrors).length > 0) {
        const fe: Record<string, string> = {};
        for (const [k, msgs] of Object.entries(err.fieldErrors)) fe[k.charAt(0).toLowerCase() + k.slice(1)] = msgs.join(' ');
        setErrors(fe);
        setServerError('Please fix the highlighted fields.');
      } else {
        setServerError(errorMessage(err, 'Registration failed. Please try again.'));
      }
    } finally {
      setLoading(false);
    }
  };

  const err = (k: string) => (errors[k] ? <div className="validation-message">{errors[k]}</div> : null);
  const deptOptions = (
    <>
      <option value="">Select department…</option>
      {DEPARTMENTS.map((d, dIdx) => (
        <option key={`${d}-${dIdx}`} value={d}>
          {d}
        </option>
      ))}
    </>
  );

  return (
    <div className="login-container">
      <div className="login-card register-card">
        <div className="login-header">
          <h1>Create your account</h1>
          <p>Join UniSphere 360+ in a minute</p>
        </div>

        {serverError && <div className="alert alert-error">{serverError}</div>}

        <form onSubmit={submit} noValidate>
          <h3 className="form-section-title">I am a</h3>
          <div className="role-selector">
            {ROLES.map((r, rIdx) => (
              <button key={`${r}-${rIdx}`} type="button" className={`role-option${form.role === r ? ' selected' : ''}`} onClick={() => selectRole(r)}>
                {r}
              </button>
            ))}
          </div>

          <div className="form-group">
            <label htmlFor="university">University</label>
            <select id="university" value={form.universityId} onChange={(e) => set('universityId', e.target.value)}>
              <option value="">Select university…</option>
              {universities.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.name}
                </option>
              ))}
            </select>
            {err('universityId')}
          </div>

          <h3 className="form-section-title">Personal info</h3>
          <div className="form-row">
            <div className="form-group">
              <label htmlFor="firstName">First name</label>
              <input id="firstName" value={form.firstName} onChange={(e) => set('firstName', e.target.value)} placeholder="Mazen" />
              {err('firstName')}
            </div>
            <div className="form-group">
              <label htmlFor="lastName">Last name</label>
              <input id="lastName" value={form.lastName} onChange={(e) => set('lastName', e.target.value)} placeholder="Mohamed" />
              {err('lastName')}
            </div>
          </div>
          <div className="form-row">
            <div className="form-group">
              <label htmlFor="middleName">Middle name (optional)</label>
              <input id="middleName" value={form.middleName} onChange={(e) => set('middleName', e.target.value)} placeholder="Optional" />
              {err('middleName')}
            </div>
            <div className="form-group">
              <label htmlFor="age">Age</label>
              <input
                id="age"
                type="number"
                min={MIN_AGE[form.role]}
                max={100}
                value={form.age}
                onChange={(e) => set('age', e.target.value)}
                placeholder={String(MIN_AGE[form.role])}
              />
              <small className="field-hint">
                Must be at least {MIN_AGE[form.role]} for a {form.role}.
              </small>
              {err('age')}
            </div>
          </div>
          <div className="form-group">
            <label htmlFor="gender">Gender</label>
            <select id="gender" value={form.gender} onChange={(e) => set('gender', e.target.value)}>
              <option value="">Select gender…</option>
              <option value="Male">Male</option>
              <option value="Female">Female</option>
            </select>
            {err('gender')}
          </div>

          {form.role === 'Student' && (
            <>
              <h3 className="form-section-title">Student details</h3>
              <div className="form-row">
                <div className="form-group">
                  <label htmlFor="academicYear">Academic year</label>
                  <select id="academicYear" value={form.academicYear} onChange={(e) => set('academicYear', e.target.value)}>
                    <option value="">Select year…</option>
                    {[1, 2, 3, 4, 5].map((y, yIdx) => (
                      <option key={`${y}-${yIdx}`} value={y}>
                        Year {y}
                      </option>
                    ))}
                  </select>
                  {err('academicYear')}
                </div>
                <div className="form-group">
                  <label htmlFor="department">Department</label>
                  <select id="department" value={form.department} onChange={(e) => set('department', e.target.value)}>
                    {deptOptions}
                  </select>
                  {err('department')}
                </div>
              </div>
            </>
          )}

          {form.role === 'Doctor' && (
            <>
              <h3 className="form-section-title">Doctor details</h3>
              <div className="form-row">
                <div className="form-group">
                  <label htmlFor="specialization">Specialization / department you teach</label>
                  <input
                    id="specialization"
                    value={form.specialization}
                    onChange={(e) => set('specialization', e.target.value)}
                    placeholder="e.g. Artificial Intelligence"
                  />
                  {err('specialization')}
                </div>
                <div className="form-group">
                  <label htmlFor="doctorDepartment">Department (optional)</label>
                  <select id="doctorDepartment" value={form.department} onChange={(e) => set('department', e.target.value)}>
                    {deptOptions}
                  </select>
                </div>
              </div>
            </>
          )}

          {form.role === 'Graduate' && (
            <>
              <h3 className="form-section-title">Graduate details</h3>
              <div className="form-group">
                <label htmlFor="graduatedFrom">Graduated from (university / faculty)</label>
                <input
                  id="graduatedFrom"
                  value={form.graduatedFrom}
                  onChange={(e) => set('graduatedFrom', e.target.value)}
                  placeholder="e.g. Cairo University, Faculty of Engineering"
                />
                {err('graduatedFrom')}
              </div>
              <div className="form-row">
                <div className="form-group">
                  <label htmlFor="graduationYear">Graduation year</label>
                  <input id="graduationYear" type="number" value={form.graduationYear} onChange={(e) => set('graduationYear', e.target.value)} placeholder="2023" />
                  {err('graduationYear')}
                </div>
                <div className="form-group">
                  <label htmlFor="gradDepartment">Department</label>
                  <select id="gradDepartment" value={form.department} onChange={(e) => set('department', e.target.value)}>
                    {deptOptions}
                  </select>
                  {err('department')}
                </div>
              </div>
            </>
          )}

          <h3 className="form-section-title">Contact</h3>
          <div className="form-row">
            <div className="form-group">
              <label htmlFor="phone">Phone number</label>
              <input id="phone" value={form.phoneNumber} onChange={(e) => set('phoneNumber', e.target.value)} placeholder="01012345678" />
              {err('phoneNumber')}
            </div>
            <div className="form-group">
              <label htmlFor="regEmail">Email</label>
              <input id="regEmail" type="email" value={form.email} onChange={(e) => set('email', e.target.value)} placeholder="you@unisphere.edu" />
              {err('email')}
            </div>
          </div>
          <div className="form-group">
            <label htmlFor="address">Address (optional)</label>
            <input id="address" value={form.address} onChange={(e) => set('address', e.target.value)} placeholder="Street, city" />
            <small className="field-hint">Locked after registration; an administrator can update it later.</small>
            {err('address')}
          </div>

          <h3 className="form-section-title">Account</h3>
          <div className="form-row">
            <div className="form-group">
              <label htmlFor="regPassword">Password</label>
              <input
                id="regPassword"
                type="password"
                value={form.password}
                onChange={(e) => set('password', e.target.value)}
                placeholder="Min 8 chars, upper + lower + digit + symbol"
                autoComplete="new-password"
              />
              {err('password')}
            </div>
            <div className="form-group">
              <label htmlFor="confirmPassword">Confirm password</label>
              <input
                id="confirmPassword"
                type="password"
                value={form.confirmPassword}
                onChange={(e) => set('confirmPassword', e.target.value)}
                placeholder="Repeat your password"
                autoComplete="new-password"
              />
              {err('confirmPassword')}
            </div>
          </div>

          <button type="submit" className="btn btn-primary" disabled={loading}>
            {loading ? 'Creating account…' : 'Register'}
          </button>
        </form>

        <div className="auth-switch">
          <span>
            Already have an account? <Link to="/login">Sign in</Link>
          </span>
        </div>
      </div>
    </div>
  );
}
