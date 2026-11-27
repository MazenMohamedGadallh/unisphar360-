import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { UiIcon } from "../UiIcon";
import { ThemeToggle } from "../components/ThemeToggle";
import { Alert } from "../components/ui";
import { LoginCampusScene } from "./LoginCampusScene";
import "./register.css";

import { API_BASE_URL } from "../lib/api";
const TOKEN_KEY = "unisphere.auth.token";

const Roles = ["Student", "Doctor", "Graduate"];

const RoleCards: Record<string, { icon: string; description: string }> = {
  Student: { icon: "graduation-cap", description: "Courses, planner and campus life" },
  Doctor: { icon: "microscope", description: "Teach, publish and manage courses" },
  Graduate: { icon: "award", description: "Alumni network and career tools" },
};

const Departments = [
  "Computer Science",
  "Information Systems",
  "Artificial Intelligence",
  "Engineering",
  "Business",
  "Medicine",
  "Other",
];

const EgyptianPhone = /^(\+20|0)?1[0125]\d{8}$/;
const InternationalPhone = /^\+?[1-9]\d{7,14}$/;
const PasswordPattern = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).{8,}$/;
const EmailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

interface AuthUser {
  id: number;
  fullName: string;
  email: string;
  roleId: number;
  role: string;
  major: string;
  avatarUrl?: string | null;
}

interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  user: AuthUser;
}

interface UniversityDto {
  id: number;
  name: string;
  logo?: string | null;
}

interface RegisterPayload {
  firstName: string;
  middleName: string | null;
  lastName: string;
  phoneNumber: string;
  email: string;
  gender: string;
  age: number;
  role: string;
  password: string;
  confirmPassword: string;
  academicYear: number | null;
  department: string | null;
  specialization: string | null;
  graduatedFrom: string | null;
  graduationYear: number | null;
  universityId: number | null;
  address: string | null;
}

interface RegisterResult {
  succeeded: boolean;
  token?: string;
  user?: AuthUser;
  fieldErrors?: Record<string, string[]>;
  errorMessage?: string;
}

function getToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY);
  } catch {
    return null;
  }
}

function saveToken(token: string): void {
  try {
    localStorage.setItem(TOKEN_KEY, token);
  } catch {
    // Storage unavailable (private mode): keep the token in memory only.
  }
}

function clearToken(): void {
  try {
    localStorage.removeItem(TOKEN_KEY);
  } catch {
    // Storage unavailable: nothing to clear.
  }
}

function tokenExpiry(token: string): number | null {
  try {
    const payload = token.split(".")[1];
    const decoded = decodeURIComponent(
      atob(payload)
        .split("")
        .map((c) => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2))
        .join(""),
    );
    const data = JSON.parse(decoded) as { exp?: number };
    return typeof data.exp === "number" ? data.exp * 1000 : null;
  } catch {
    return null;
  }
}

function isTokenUsable(token: string | null): boolean {
  if (!token) return false;
  const expiry = tokenExpiry(token);
  return expiry !== null && expiry > Date.now();
}

async function fetchMe(token: string): Promise<AuthUser | null> {
  const response = await fetch(`${API_BASE_URL}/api/auth/me`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok) return null;
  return (await response.json()) as AuthUser;
}

async function register(payload: RegisterPayload): Promise<RegisterResult> {
  const response = await fetch(`${API_BASE_URL}/api/auth/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });

  if (response.ok) {
    const data = (await response.json()) as AuthResponse;
    return { succeeded: true, token: data.token, user: data.user };
  }

  if (response.status === 409) {
    const text = (await response.text()).trim().replace(/^"|"$/g, "");
    return { succeeded: false, errorMessage: text || "This email is already registered." };
  }

  let fieldErrors: Record<string, string[]> | undefined;
  let errorMessage: string | undefined;
  try {
    const body = (await response.json()) as { error?: string; errors?: Record<string, string[]> };
    fieldErrors = body?.errors;
    errorMessage = body?.error;
  } catch {
    // Not a JSON error body: fall through to the default message.
  }

  return {
    succeeded: false,
    fieldErrors,
    errorMessage: errorMessage ?? "Registration failed. Please try again.",
  };
}

function homeFor(role: string | null | undefined): string {
  switch ((role ?? "").trim().toLowerCase()) {
    case "student":
      return "/dashboard";
    case "doctor":
      return "/doctor";
    case "graduate":
      return "/graduate";
    // Aligned with the backend canonical RoleRoutes.HomeFor (admin -> /admin);
    // unreachable in practice because Admin can never register.
    case "admin":
      return "/admin";
    default:
      return "/login";
  }
}

export function Register() {
  const navigate = useNavigate();

  const [role, setRole] = useState("Student");
  const [universities, setUniversities] = useState<UniversityDto[]>([]);
  const [universityId, setUniversityId] = useState("");
  const [firstName, setFirstName] = useState("");
  const [middleName, setMiddleName] = useState("");
  const [lastName, setLastName] = useState("");
  const [age, setAge] = useState("");
  const [gender, setGender] = useState("");
  const [academicYear, setAcademicYear] = useState("");
  const [department, setDepartment] = useState("");
  const [specialization, setSpecialization] = useState("");
  const [graduatedFrom, setGraduatedFrom] = useState("");
  const [graduationYear, setGraduationYear] = useState("");
  const [phoneNumber, setPhoneNumber] = useState("");
  const [email, setEmail] = useState("");
  const [address, setAddress] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);

  const [serverError, setServerError] = useState<string | null>(null);
  const [serverFields, setServerFields] = useState<Record<string, string[]> | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(false);

  const minAge = role === "Doctor" ? 25 : role === "Graduate" ? 22 : 18;

  // Display-only checklist derived from the same PasswordPattern that
  // validateForm() enforces; it never changes validation behavior.
  const passwordChecks: Array<{ label: string; met: boolean }> = [
    { label: "8+ characters", met: password.length >= 8 },
    { label: "Upper-case letter", met: /[A-Z]/.test(password) },
    { label: "Lower-case letter", met: /[a-z]/.test(password) },
    { label: "Digit", met: /\d/.test(password) },
    { label: "Symbol", met: /[^a-zA-Z0-9]/.test(password) },
  ];

  useEffect(() => {
    const token = getToken();
    if (!token) return;
    if (!isTokenUsable(token)) {
      clearToken();
      return;
    }
    // A usable token means the user is signed in: go to the landing page
    // instead of showing the register form again.
    void (async () => {
      const user = await fetchMe(token);
      if (user) navigate(homeFor(user.role), { replace: true });
    })();
    // navigate is stable in react-router v7, so this behaves like run-once.
  }, [navigate]);

  useEffect(() => {
    document.title = "Register - UniSphere 360+";
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/api/auth/universities`);
        if (!response.ok) return;
        const data = (await response.json()) as UniversityDto[];
        if (cancelled) return;
        setUniversities(data);
        if (data.length > 0) setUniversityId(String(data[0].id));
      } catch {
        // Keep the empty list: the select just shows the placeholder.
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  function validateForm(): Record<string, string> {
    const errors: Record<string, string> = {};
    const first = firstName.trim();
    if (!first) errors.FirstName = "First name is required.";
    else if (first.length > 100) errors.FirstName = "First name is too long.";

    const last = lastName.trim();
    if (!last) errors.LastName = "Last name is required.";
    else if (last.length > 100) errors.LastName = "Last name is too long.";

    const middle = middleName.trim();
    if (middle && middle.length > 100) errors.MiddleName = "Middle name is too long.";

    const phone = phoneNumber.trim();
    if (!phone) errors.PhoneNumber = "Phone number is required.";
    else if (!EgyptianPhone.test(phone) && !InternationalPhone.test(phone))
      errors.PhoneNumber = "Enter a valid phone number (e.g. 01012345678 or +201012345678).";

    const mail = email.trim();
    if (!mail) errors.Email = "Email is required.";
    else if (!EmailPattern.test(mail)) errors.Email = "Enter a valid email address.";

    if (!gender) errors.Gender = "Gender is required.";

    if (!age) errors.Age = "Age is required.";
    else {
      const ageNum = Number(age);
      if (ageNum < minAge || ageNum > 100)
        errors.Age = `Minimum age for a ${role} is ${minAge}. Maximum age is 100.`;
    }

    if (!password) errors.Password = "Password is required.";
    else if (!PasswordPattern.test(password))
      errors.Password =
        "Password must be at least 8 characters and include an upper-case letter, a lower-case letter, a digit and a symbol.";

    if (!confirmPassword) errors.ConfirmPassword = "Please confirm your password.";
    else if (confirmPassword !== password) errors.ConfirmPassword = "Passwords do not match.";

    if (role === "Student") {
      if (!academicYear) errors.AcademicYear = "Academic year is required (1-5).";
      else if (Number(academicYear) < 1 || Number(academicYear) > 5)
        errors.AcademicYear = "Academic year must be between 1 and 5.";
      if (!department) errors.Department = "Department is required.";
    }

    if (role === "Doctor") {
      if (!specialization.trim()) errors.Specialization = "Specialization is required for doctors.";
    }

    if (role === "Graduate") {
      if (!graduatedFrom.trim()) errors.GraduatedFrom = "Graduated from is required for graduates.";
      if (!graduationYear) errors.GraduationYear = `Graduation year must be between 1950 and ${new Date().getFullYear()}.`;
      if (!department) errors.Department = "Department is required for graduates.";
    }

    return errors;
  }

  function fieldError(key: string): string | undefined {
    const server = serverFields?.[key]?.[0];
    return server ?? fieldErrors[key];
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setServerError(null);
    setServerFields(null);

    const errors = validateForm();
    setFieldErrors(errors);
    if (Object.keys(errors).length > 0) return;

    setLoading(true);
    try {
      const result = await register({
        firstName: firstName.trim(),
        middleName: middleName.trim() || null,
        lastName: lastName.trim(),
        phoneNumber: phoneNumber.trim(),
        email: email.trim(),
        gender,
        age: age === "" ? 0 : Number(age),
        role,
        password,
        confirmPassword,
        academicYear: academicYear ? Number(academicYear) : null,
        department: department.trim() || null,
        specialization: specialization.trim() || null,
        graduatedFrom: graduatedFrom.trim() || null,
        graduationYear: graduationYear ? Number(graduationYear) : null,
        universityId: universityId ? Number(universityId) : null,
        address: address.trim() || null,
      });

      if (result.succeeded) {
        // The API returns a token, so the user is logged in right away.
        if (result.token && result.user) {
          saveToken(result.token);
          navigate(homeFor(result.user.role), { replace: true });
        } else {
          navigate("/login", { replace: true });
        }
        return;
      }

      setServerFields(result.fieldErrors ?? null);
      setServerError(result.errorMessage ?? "Registration failed. Please try again.");
    } catch {
      setServerError("Registration failed. Please try again.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="register-shell">
      {/* Brand panel (desktop only; the mobile header sits in the form panel). */}
      <aside className="register-brand" aria-hidden="true">
        <LoginCampusScene />
        <img
          src="/src/assets/images/university_campus_hero_1795788262420.jpg"
          alt=""
          referrerPolicy="no-referrer"
          className="register-brand-photo"
          onError={(e) => {
            e.currentTarget.style.display = "none";
          }}
        />
        <div className="register-brand-scrim" />
        <div className="register-brand-dots" />
        <div className="register-brand-inner">
          <div className="register-brand-logo">
            <UiIcon name="diamond" />
            <span className="register-brand-title">UniSphere <span>360+</span></span>
          </div>
          <div>
            <h2 className="register-brand-heading">Start your journey in a minute.</h2>
            <p className="register-brand-sub">
              One account for courses, planning, career tools and AI assistance — across every stage of student life.
            </p>
          </div>
          <div className="register-features">
            <div className="register-feature">
              <span className="register-feature-icon"><UiIcon name="sparkles" /></span>
              <span>
                AI Assistant
                <small>Chat, course-grounded answers and study help</small>
              </span>
            </div>
            <div className="register-feature">
              <span className="register-feature-icon"><UiIcon name="graduation-cap" /></span>
              <span>
                Academic Advisor
                <small>Performance insights and course recommendations</small>
              </span>
            </div>
            <div className="register-feature">
              <span className="register-feature-icon"><UiIcon name="briefcase" /></span>
              <span>
                Career tools
                <small>CV studio, job matches and interview coaching</small>
              </span>
            </div>
          </div>
        </div>
        <p className="register-brand-footer">UniSphere 360+ · Student success platform</p>
      </aside>

      {/* Form panel */}
      <main className="register-panel">
        <div className="register-theme-toggle">
          <ThemeToggle />
        </div>

        <div className="register-mobile-brand">
          <span className="text-primary"><UiIcon name="diamond" /></span>
          <span>UniSphere <span className="text-primary">360+</span></span>
        </div>

        <div className="register-card">
          <h1 className="register-heading">Create your account</h1>
          <p className="register-sub">Join UniSphere 360+ in a minute</p>

          {serverError && <Alert kind="error">{serverError}</Alert>}

          <form onSubmit={handleSubmit}>
            <h3 className="register-section-title">I am a</h3>
            <div className="register-role-grid" role="radiogroup" aria-label="I am a">
              {Roles.map((r) => (
                <button
                  key={r}
                  type="button"
                  role="radio"
                  aria-checked={role === r}
                  onClick={() => setRole(r)}
                  className={role === r ? "register-role-card is-selected" : "register-role-card"}
                >
                  <span className="register-role-icon"><UiIcon name={RoleCards[r].icon} /></span>
                  <span className="register-role-title">{r}</span>
                  <span className="register-role-desc">{RoleCards[r].description}</span>
                </button>
              ))}
            </div>

            <div className="register-field">
              <label htmlFor="university" className="register-label">University</label>
              <select
                id="university"
                value={universityId}
                onChange={(e) => setUniversityId(e.target.value)}
                className="register-input"
              >
                <option value="">Select university...</option>
                {universities.map((u) => (
                  <option key={u.id} value={String(u.id)}>
                    {u.name}
                  </option>
                ))}
              </select>
              {fieldError("UniversityId") && (
                <div className="register-field-error">{fieldError("UniversityId")}</div>
              )}
            </div>

            <h3 className="register-section-title">Personal info</h3>
            <div className="register-grid-2">
              <div className="register-field">
                <label htmlFor="firstName" className="register-label">First name</label>
                <input
                  id="firstName"
                  type="text"
                  value={firstName}
                  onChange={(e) => setFirstName(e.target.value)}
                  placeholder="Mazen"
                  className="register-input"
                />
                {fieldError("FirstName") && (
                  <div className="register-field-error">{fieldError("FirstName")}</div>
                )}
              </div>
              <div className="register-field">
                <label htmlFor="lastName" className="register-label">Last name</label>
                <input
                  id="lastName"
                  type="text"
                  value={lastName}
                  onChange={(e) => setLastName(e.target.value)}
                  placeholder="Mohamed"
                  className="register-input"
                />
                {fieldError("LastName") && (
                  <div className="register-field-error">{fieldError("LastName")}</div>
                )}
              </div>
            </div>
            <div className="register-grid-2">
              <div className="register-field">
                <label htmlFor="middleName" className="register-label">Middle name (optional)</label>
                <input
                  id="middleName"
                  type="text"
                  value={middleName}
                  onChange={(e) => setMiddleName(e.target.value)}
                  placeholder="Optional"
                  className="register-input"
                />
                {fieldError("MiddleName") && (
                  <div className="register-field-error">{fieldError("MiddleName")}</div>
                )}
              </div>
              <div className="register-field">
                <label htmlFor="age" className="register-label">Age</label>
                <input
                  id="age"
                  type="number"
                  min={minAge}
                  max={100}
                  value={age}
                  onChange={(e) => setAge(e.target.value)}
                  placeholder={String(minAge)}
                  className="register-input"
                />
                <small className="register-hint">Must be at least {minAge} for a {role}.</small>
                {fieldError("Age") && <div className="register-field-error">{fieldError("Age")}</div>}
              </div>
            </div>
            <div className="register-field">
              <label htmlFor="gender" className="register-label">Gender</label>
              <select
                id="gender"
                value={gender}
                onChange={(e) => setGender(e.target.value)}
                className="register-input"
              >
                <option value="">Select gender...</option>
                <option value="Male">Male</option>
                <option value="Female">Female</option>
              </select>
              {fieldError("Gender") && <div className="register-field-error">{fieldError("Gender")}</div>}
            </div>

            {role === "Student" && (
              <>
                <h3 className="register-section-title">Student details</h3>
                <div className="register-grid-2">
                  <div className="register-field">
                    <label htmlFor="academicYear" className="register-label">Academic year</label>
                    <select
                      id="academicYear"
                      value={academicYear}
                      onChange={(e) => setAcademicYear(e.target.value)}
                      className="register-input"
                    >
                      <option value="">Select year...</option>
                      {[1, 2, 3, 4, 5].map((y) => (
                        <option key={y} value={String(y)}>
                          Year {y}
                        </option>
                      ))}
                    </select>
                    {fieldError("AcademicYear") && (
                      <div className="register-field-error">{fieldError("AcademicYear")}</div>
                    )}
                  </div>
                  <div className="register-field">
                    <label htmlFor="department" className="register-label">Department</label>
                    <select
                      id="department"
                      value={department}
                      onChange={(e) => setDepartment(e.target.value)}
                      className="register-input"
                    >
                      <option value="">Select department...</option>
                      {Departments.map((d) => (
                        <option key={d} value={d}>
                          {d}
                        </option>
                      ))}
                    </select>
                    {fieldError("Department") && (
                      <div className="register-field-error">{fieldError("Department")}</div>
                    )}
                  </div>
                </div>
              </>
            )}

            {role === "Doctor" && (
              <>
                <h3 className="register-section-title">Doctor details</h3>
                <div className="register-grid-2">
                  <div className="register-field">
                    <label htmlFor="specialization" className="register-label">Specialization / department you teach</label>
                    <input
                      id="specialization"
                      type="text"
                      value={specialization}
                      onChange={(e) => setSpecialization(e.target.value)}
                      placeholder="e.g. Artificial Intelligence"
                      className="register-input"
                    />
                    {fieldError("Specialization") && (
                      <div className="register-field-error">{fieldError("Specialization")}</div>
                    )}
                  </div>
                  <div className="register-field">
                    <label htmlFor="doctorDepartment" className="register-label">Department (optional)</label>
                    <select
                      id="doctorDepartment"
                      value={department}
                      onChange={(e) => setDepartment(e.target.value)}
                      className="register-input"
                    >
                      <option value="">Select department...</option>
                      {Departments.map((d) => (
                        <option key={d} value={d}>
                          {d}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
              </>
            )}

            {role === "Graduate" && (
              <>
                <h3 className="register-section-title">Graduate details</h3>
                <div className="register-field">
                  <label htmlFor="graduatedFrom" className="register-label">Graduated from (university / faculty)</label>
                  <input
                    id="graduatedFrom"
                    type="text"
                    value={graduatedFrom}
                    onChange={(e) => setGraduatedFrom(e.target.value)}
                    placeholder="e.g. Cairo University, Faculty of Engineering"
                    className="register-input"
                  />
                  {fieldError("GraduatedFrom") && (
                    <div className="register-field-error">{fieldError("GraduatedFrom")}</div>
                  )}
                </div>
                <div className="register-grid-2">
                  <div className="register-field">
                    <label htmlFor="graduationYear" className="register-label">Graduation year</label>
                    <input
                      id="graduationYear"
                      type="number"
                      value={graduationYear}
                      onChange={(e) => setGraduationYear(e.target.value)}
                      placeholder="2023"
                      className="register-input"
                    />
                    {fieldError("GraduationYear") && (
                      <div className="register-field-error">{fieldError("GraduationYear")}</div>
                    )}
                  </div>
                  <div className="register-field">
                    <label htmlFor="gradDepartment" className="register-label">Department</label>
                    <select
                      id="gradDepartment"
                      value={department}
                      onChange={(e) => setDepartment(e.target.value)}
                      className="register-input"
                    >
                      <option value="">Select department...</option>
                      {Departments.map((d) => (
                        <option key={d} value={d}>
                          {d}
                        </option>
                      ))}
                    </select>
                    {fieldError("Department") && (
                      <div className="register-field-error">{fieldError("Department")}</div>
                    )}
                  </div>
                </div>
              </>
            )}

            <h3 className="register-section-title">Contact</h3>
            <div className="register-grid-2">
              <div className="register-field">
                <label htmlFor="phone" className="register-label">Phone number</label>
                <input
                  id="phone"
                  type="text"
                  value={phoneNumber}
                  onChange={(e) => setPhoneNumber(e.target.value)}
                  placeholder="01012345678"
                  className="register-input"
                />
                {fieldError("PhoneNumber") && (
                  <div className="register-field-error">{fieldError("PhoneNumber")}</div>
                )}
              </div>
              <div className="register-field">
                <label htmlFor="email" className="register-label">Email</label>
                <div className="register-input-wrap">
                  <span className="register-input-icon"><UiIcon name="mail" /></span>
                  <input
                    id="email"
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="you@unisphere.edu"
                    className="register-input has-icon"
                  />
                </div>
                {fieldError("Email") && <div className="register-field-error">{fieldError("Email")}</div>}
              </div>
            </div>
            <div className="register-field">
              <label htmlFor="address" className="register-label">Address (optional)</label>
              <input
                id="address"
                type="text"
                value={address}
                onChange={(e) => setAddress(e.target.value)}
                placeholder="Street, city"
                className="register-input"
              />
              <small className="register-hint">Locked after registration; an administrator can update it later.</small>
              {fieldError("Address") && <div className="register-field-error">{fieldError("Address")}</div>}
            </div>

            <h3 className="register-section-title">Account</h3>
            <div className="register-grid-2">
              <div className="register-field">
                <label htmlFor="password" className="register-label">Password</label>
                <div className="register-input-wrap">
                  <span className="register-input-icon"><UiIcon name="lock" /></span>
                  <input
                    id="password"
                    type={showPassword ? "text" : "password"}
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="Min 8 chars, upper + lower + digit + symbol"
                    className="register-input has-icon has-eye"
                  />
                  <button
                    type="button"
                    className="register-eye"
                    onClick={() => setShowPassword((visible) => !visible)}
                    aria-label={showPassword ? "Hide password" : "Show password"}
                    title={showPassword ? "Hide password" : "Show password"}
                  >
                    <UiIcon name={showPassword ? "eye-off" : "eye"} />
                  </button>
                </div>
                {password.length > 0 && (
                  <div className="register-pw-checks">
                    {passwordChecks.map((check) => (
                      <span
                        key={check.label}
                        className={check.met ? "register-pw-chip met" : "register-pw-chip"}
                      >
                        {check.label}
                      </span>
                    ))}
                  </div>
                )}
                {fieldError("Password") && <div className="register-field-error">{fieldError("Password")}</div>}
              </div>
              <div className="register-field">
                <label htmlFor="confirmPassword" className="register-label">Confirm password</label>
                <div className="register-input-wrap">
                  <span className="register-input-icon"><UiIcon name="lock" /></span>
                  <input
                    id="confirmPassword"
                    type={showConfirmPassword ? "text" : "password"}
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    placeholder="Repeat your password"
                    className="register-input has-icon has-eye"
                  />
                  <button
                    type="button"
                    className="register-eye"
                    onClick={() => setShowConfirmPassword((visible) => !visible)}
                    aria-label={showConfirmPassword ? "Hide password" : "Show password"}
                    title={showConfirmPassword ? "Hide password" : "Show password"}
                  >
                    <UiIcon name={showConfirmPassword ? "eye-off" : "eye"} />
                  </button>
                </div>
                {fieldError("ConfirmPassword") && (
                  <div className="register-field-error">{fieldError("ConfirmPassword")}</div>
                )}
              </div>
            </div>

            <button type="submit" className="register-submit" disabled={loading}>
              {loading ? (
                <>
                  <span className="register-spinner" aria-hidden="true" />
                  Creating account...
                </>
              ) : (
                "Register"
              )}
            </button>
          </form>

          <p className="register-signin">
            Already have an account? <Link to="/login">Sign in</Link>
          </p>
        </div>
      </main>
    </div>
  );
}
