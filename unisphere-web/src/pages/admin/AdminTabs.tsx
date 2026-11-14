// Tabs of the Admin Panel. Every endpoint here is [Authorize(Roles = "Admin")].

import { useEffect, useState, type FormEvent } from 'react';
import { Alert, Async, ConfirmDialog, Empty, ErrorBox, Modal, Pagination, StatusBadge } from '../../components/ui';
import { api, qs } from '../../lib/api';
import { useAction, useApi, useDebounced, useFlash } from '../../lib/hooks';
import { fmtDate, fmtDateTime, fromDateTimeInput, toDateTimeInput } from '../../lib/format';
import type {
  AdminCourseDto,
  AdminDoctorDto,
  AdminOverviewDto,
  AdminUserDetailDto,
  AdminUserDto,
  AtRiskStudentDto,
  AuditLogDto,
  DepartmentStatsDto,
  EventDto,
  GraduationApplicationAdminDto,
  InstructorAssignmentDto,
  PagedResultDto,
  SystemHealthDto,
  UniversityDto,
} from '../../types/dto';
import type { AdminAnnouncement } from '../../types/models';

const ROLES = ['Student', 'Graduate', 'Doctor', 'Admin'];

// ---------------- Overview ----------------

export function OverviewTab() {
  const res = useApi<AdminOverviewDto>('/api/admin/overview');
  return (
    <Async res={res} loadingLabel="Loading overview…">
      {(o) => {
        const max = Math.max(1, ...o.loginsPerDay.map((d) => d.count));
        const bw = o.loginsPerDay.length ? 300 / o.loginsPerDay.length : 0;
        const kpis: Array<[string, string | number]> = [
          ['Students', o.totalStudents],
          ['Graduates', o.totalGraduates],
          ['Doctors', o.totalDoctors],
          ['Admins', o.totalAdmins],
          ['New (7d / 30d)', `${o.newRegistrations7d} / ${o.newRegistrations30d}`],
          ['Active today', o.activeUsersToday],
          ['Avg GPA', o.averageGpa.toFixed(2)],
          ['Avg attendance', `${Math.round(o.averageAttendance)}%`],
          ['Courses', o.coursesCount],
          ['On track', o.onTrackStudents],
          ['Needs attention', o.needsAttentionStudents],
          ['At risk', o.atRiskStudents],
        ];
        return (
          <>
            <div className="summary-cards admin-kpis">
              {kpis.map(([label, value]) => (
                <div className="card" key={label}>
                  <div className="card-info">
                    <span className="card-label">{label}</span>
                    <span className="card-value">{value}</span>
                  </div>
                </div>
              ))}
            </div>
            <div className="card section-gap-top">
              <div className="card-header">
                <h3>Logins per day (last 14 days)</h3>
              </div>
              <div className="chart-container">
                {o.loginsPerDay.length === 0 ? (
                  <div className="empty-state">No logins recorded yet.</div>
                ) : (
                  <svg viewBox="0 0 340 170" className="trend-chart" role="img" aria-label="Logins per day">
                    {o.loginsPerDay.map((d, i) => {
                      const h = (130 * d.count) / max;
                      const x = 30 + i * bw;
                      const date = new Date(d.date);
                      return (
                        <g key={`${d.date}-${i}`}>
                          <rect x={x + 2} y={140 - h} width={Math.max(1, bw - 4)} height={h} className="column-bar" rx="2">
                            <title>{`${d.count} login(s)`}</title>
                          </rect>
                          <text x={x + bw / 2} y="155" textAnchor="middle" className="trend-label">
                            {`${String(date.getMonth() + 1).padStart(2, '0')}/${String(date.getDate()).padStart(2, '0')}`}
                          </text>
                          {d.count > 0 && (
                            <text x={x + bw / 2} y={136 - h} textAnchor="middle" className="trend-value">
                              {d.count}
                            </text>
                          )}
                        </g>
                      );
                    })}
                  </svg>
                )}
              </div>
            </div>
          </>
        );
      }}
    </Async>
  );
}

// ---------------- Users ----------------

function EditUserModal({ userId, onClose, onSaved }: { userId: number; onClose: () => void; onSaved: () => void }) {
  const detail = useApi<AdminUserDetailDto>(`/api/admin/users/${userId}`);
  const universities = useApi<UniversityDto[]>('/api/auth/universities');
  const [f, setF] = useState<{ firstName: string; middleName: string; lastName: string; email: string; phoneNumber: string; address: string; department: string; universityId: string } | null>(null);
  const action = useAction();

  useEffect(() => {
    const d = detail.data;
    if (d)
      setF({
        firstName: d.firstName,
        middleName: d.middleName ?? '',
        lastName: d.lastName,
        email: d.email,
        phoneNumber: d.phoneNumber,
        address: d.address,
        department: d.department,
        universityId: d.universityId ? String(d.universityId) : '',
      });
  }, [detail.data]);

  const save = (e: FormEvent) => {
    e.preventDefault();
    if (!f) return;
    void action.run(async () => {
      await api.put(`/api/admin/users/${userId}`, {
        firstName: f.firstName.trim(),
        middleName: f.middleName.trim() || null,
        lastName: f.lastName.trim(),
        email: f.email.trim(),
        phoneNumber: f.phoneNumber.trim(),
        address: f.address.trim() || null,
        department: f.department.trim() || null,
        universityId: f.universityId ? Number(f.universityId) : null,
      });
      onSaved();
    });
  };

  return (
    <Modal title={detail.data ? `Edit ${detail.data.role} account` : 'Edit account'} onClose={onClose} wide>
      <ErrorBox error={action.error || detail.error} />
      {!f ? (
        <div className="loading">Loading…</div>
      ) : (
        <form onSubmit={save}>
          <p className="course-meta">Locked identity fields for this account. Every change is audit-logged; role and password are managed separately.</p>
          <div className="form-row">
            <div className="form-group">
              <label>First name</label>
              <input value={f.firstName} onChange={(e) => setF({ ...f, firstName: e.target.value })} maxLength={100} required />
            </div>
            <div className="form-group">
              <label>Middle name (optional)</label>
              <input value={f.middleName} onChange={(e) => setF({ ...f, middleName: e.target.value })} maxLength={100} />
            </div>
          </div>
          <div className="form-row">
            <div className="form-group">
              <label>Last name</label>
              <input value={f.lastName} onChange={(e) => setF({ ...f, lastName: e.target.value })} maxLength={100} required />
            </div>
            <div className="form-group">
              <label>Email</label>
              <input type="email" value={f.email} onChange={(e) => setF({ ...f, email: e.target.value })} maxLength={200} required />
            </div>
          </div>
          <div className="form-row">
            <div className="form-group">
              <label>Phone number</label>
              <input value={f.phoneNumber} onChange={(e) => setF({ ...f, phoneNumber: e.target.value })} maxLength={20} />
            </div>
            <div className="form-group">
              <label>Department</label>
              <input value={f.department} onChange={(e) => setF({ ...f, department: e.target.value })} maxLength={200} />
            </div>
          </div>
          <div className="form-group">
            <label>Address</label>
            <input value={f.address} onChange={(e) => setF({ ...f, address: e.target.value })} maxLength={300} />
          </div>
          <div className="form-group">
            <label>University</label>
            <select value={f.universityId} onChange={(e) => setF({ ...f, universityId: e.target.value })}>
              <option value="">No university assigned</option>
              {(universities.data ?? []).map((u) => (
                <option key={u.id} value={u.id}>
                  {u.name}
                </option>
              ))}
            </select>
          </div>
          <div className="modal-actions flush">
            <button type="button" className="btn btn-secondary" onClick={onClose} disabled={action.busy}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" disabled={action.busy}>
              {action.busy ? 'Saving…' : 'Save changes'}
            </button>
          </div>
        </form>
      )}
    </Modal>
  );
}

type Confirm = { title: string; message: string; run: () => Promise<void> };

export function UsersTab() {
  const [search, setSearch] = useState('');
  const term = useDebounced(search, 350);
  const [type, setType] = useState('');
  const [status, setStatus] = useState('all');
  const [page, setPage] = useState(1);
  const users = useApi<PagedResultDto<AdminUserDto>>(`/api/admin/users/search${qs({ search: term.trim(), type, status, page, pageSize: 20 })}`);
  const [editing, setEditing] = useState<number | null>(null);
  const [confirm, setConfirm] = useState<Confirm | null>(null);
  const [flash, setFlash] = useFlash();
  const action = useAction();

  const runConfirm = () =>
    action.run(async () => {
      if (!confirm) return;
      await confirm.run();
      setConfirm(null);
      await users.reload();
    });

  return (
    <div className="card">
      <div className="card-header">
        <h3>Users</h3>
      </div>
      <ErrorBox error={action.error} />
      {flash && <Alert kind="success">{flash}</Alert>}
      <div className="form-inline">
        <div className="form-group">
          <label htmlFor="userSearch">Search</label>
          <input
            id="userSearch"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            placeholder="Name or email…"
          />
        </div>
        <div className="form-group">
          <label htmlFor="userType">Type</label>
          <select
            id="userType"
            value={type}
            onChange={(e) => {
              setType(e.target.value);
              setPage(1);
            }}
          >
            <option value="">All</option>
            {ROLES.map((r, rIdx) => (
              <option key={`${r}-${rIdx}`}>{r}</option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="userStatus">Status</label>
          <select
            id="userStatus"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
          >
            <option value="all">All</option>
            <option value="active">Active</option>
            <option value="inactive">Inactive</option>
            <option value="deleted">Deleted</option>
          </select>
        </div>
      </div>
      <Async res={users} loadingLabel="Loading users…" isEmpty={(p) => p.items.length === 0} empty="No users match the current filters.">
        {(p) => (
          <>
            <div className="table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Email</th>
                    <th>Role</th>
                    <th>Status</th>
                    <th>Last login</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {p.items.map((u) => (
                    <tr key={u.id}>
                      <td>
                        {u.fullName}
                        {u.major && <div className="muted small-print">{u.major}</div>}
                      </td>
                      <td>{u.email}</td>
                      <td>
                        <select
                          value={u.role}
                          disabled={u.isDeleted}
                          aria-label={`Role for ${u.fullName}`}
                          onChange={(e) => {
                            const role = e.target.value;
                            setConfirm({
                              title: 'Change role?',
                              message: `Change ${u.fullName} from ${u.role} to ${role}? Their next sign-in uses the new role.`,
                              run: async () => {
                                await api.patch(`/api/admin/users/${u.id}/role`, { role });
                                setFlash(`${u.fullName} is now ${role}.`);
                              },
                            });
                          }}
                        >
                          {ROLES.map((r, rIdx) => (
                            <option key={`${r}-${rIdx}`}>{r}</option>
                          ))}
                        </select>
                      </td>
                      <td>
                        {u.isDeleted ? <StatusBadge status="needs-attention" label="Deleted" /> : <StatusBadge status={u.isActive ? 'excellent' : 'good'} label={u.isActive ? 'Active' : 'Inactive'} />}
                      </td>
                      <td>{u.lastLoginAtUtc ? fmtDateTime(u.lastLoginAtUtc) : '-'}</td>
                      <td>
                        {!u.isDeleted && (
                          <div className="row-actions">
                            <button className="btn btn-sm btn-secondary" onClick={() => setEditing(u.id)}>
                              Edit details
                            </button>
                            <button
                              className={`btn btn-sm ${u.isActive ? 'btn-secondary' : 'btn-primary'}`}
                              onClick={() =>
                                setConfirm({
                                  title: u.isActive ? 'Deactivate account?' : 'Activate account?',
                                  message: u.isActive ? `${u.fullName} will no longer be able to sign in.` : `${u.fullName} will be able to sign in again.`,
                                  run: async () => {
                                    await api.patch(`/api/admin/users/${u.id}/status`, { isActive: !u.isActive });
                                    setFlash(`${u.fullName} ${u.isActive ? 'deactivated' : 'activated'}.`);
                                  },
                                })
                              }
                            >
                              {u.isActive ? 'Deactivate' : 'Activate'}
                            </button>
                            <button
                              className="btn btn-sm btn-danger"
                              onClick={() =>
                                setConfirm({
                                  title: 'Delete account?',
                                  message: `Delete ${u.fullName}? The account is soft-deleted and can no longer sign in.`,
                                  run: async () => {
                                    await api.del(`/api/admin/users/${u.id}`);
                                    setFlash(`${u.fullName} deleted.`);
                                  },
                                })
                              }
                            >
                              Delete
                            </button>
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination page={p.page} pageSize={p.pageSize} total={p.total} onPage={setPage} />
          </>
        )}
      </Async>
      {editing !== null && (
        <EditUserModal
          userId={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            setFlash('Account updated.');
            void users.reload();
          }}
        />
      )}
      {confirm && (
        <ConfirmDialog
          title={confirm.title}
          message={confirm.message}
          confirmLabel="Confirm"
          busy={action.busy}
          onConfirm={() => void runConfirm()}
          onCancel={() => {
            setConfirm(null);
            void users.reload(); // reset any role <select> that was changed then cancelled
          }}
        />
      )}
    </div>
  );
}

// ---------------- Instructor assignments ----------------

export function AssignmentsTab() {
  const universities = useApi<UniversityDto[]>('/api/auth/universities');
  const [uni, setUni] = useState('');
  const courses = useApi<AdminCourseDto[]>(uni ? `/api/admin/courses?universityId=${uni}` : null);
  const doctors = useApi<AdminDoctorDto[]>(uni ? `/api/admin/doctors?universityId=${uni}` : null);
  const assignments = useApi<InstructorAssignmentDto[]>(`/api/admin/instructor-assignments${qs({ universityId: uni })}`);
  const [courseId, setCourseId] = useState('');
  const [doctorId, setDoctorId] = useState('');
  const [term, setTerm] = useState('Fall 2025');
  const [removing, setRemoving] = useState<InstructorAssignmentDto | null>(null);
  const [flash, setFlash] = useFlash();
  const action = useAction();

  const assign = (e: FormEvent) => {
    e.preventDefault();
    void action.run(async () => {
      await api.post('/api/admin/instructor-assignments', { courseId: Number(courseId), doctorUserId: Number(doctorId), term: term.trim() });
      setCourseId('');
      setDoctorId('');
      setFlash('Doctor assigned.');
      await assignments.reload();
    });
  };

  const remove = () =>
    action.run(async () => {
      if (!removing) return;
      await api.del(`/api/admin/instructor-assignments/${removing.id}`);
      setRemoving(null);
      await assignments.reload();
    });

  return (
    <>
      <form className="card" onSubmit={assign}>
        <div className="card-header">
          <h3>Assign a doctor to a course</h3>
        </div>
        <p className="course-meta">Doctors can only be assigned to courses of their own university.</p>
        <ErrorBox error={action.error} />
        {flash && <Alert kind="success">{flash}</Alert>}
        <div className="form-inline">
          <div className="form-group">
            <label>University</label>
            <select
              value={uni}
              onChange={(e) => {
                setUni(e.target.value);
                setCourseId('');
                setDoctorId('');
              }}
            >
              <option value="">All universities</option>
              {(universities.data ?? []).map((u) => (
                <option key={u.id} value={u.id}>
                  {u.name}
                </option>
              ))}
            </select>
          </div>
          <div className="form-group">
            <label>Course</label>
            <select value={courseId} onChange={(e) => setCourseId(e.target.value)} disabled={!uni}>
              <option value="">Select course…</option>
              {(courses.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.code} — {c.title}
                </option>
              ))}
            </select>
          </div>
          <div className="form-group">
            <label>Doctor</label>
            <select value={doctorId} onChange={(e) => setDoctorId(e.target.value)} disabled={!uni}>
              <option value="">Select doctor…</option>
              {(doctors.data ?? []).map((d) => (
                <option key={d.id} value={d.id}>
                  {d.fullName}
                  {d.specialization ? ` (${d.specialization})` : ''}
                  {!d.isActive ? ' — inactive' : ''}
                </option>
              ))}
            </select>
          </div>
          <div className="form-group">
            <label>Term</label>
            <input value={term} onChange={(e) => setTerm(e.target.value)} placeholder="e.g. Fall 2025" maxLength={50} />
          </div>
          <button className="btn btn-primary" type="submit" disabled={action.busy || !courseId || !doctorId || !term.trim()}>
            {action.busy ? 'Assigning…' : 'Assign'}
          </button>
        </div>
        {!uni ? (
          <div className="empty-state">Choose a university to see its courses and doctors.</div>
        ) : (
          doctors.data && doctors.data.length === 0 && <div className="empty-state">This university has no doctors yet.</div>
        )}
      </form>
      <div className="card section-gap-top">
        <div className="card-header">
          <h3>Current assignments</h3>
        </div>
        <Async res={assignments} loadingLabel="Loading assignments…" empty="No doctor is assigned to a course yet.">
          {(list) => (
            <div className="table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Course</th>
                    <th>Doctor</th>
                    <th>Term</th>
                    <th>University</th>
                    <th>Since</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {list.map((a) => (
                    <tr key={a.id}>
                      <td>
                        {a.courseCode} — {a.courseTitle}
                      </td>
                      <td>{a.doctorName}</td>
                      <td>{a.term || '-'}</td>
                      <td>{a.universityName}</td>
                      <td>{fmtDate(a.createdAtUtc)}</td>
                      <td>
                        <button className="btn btn-sm btn-danger" onClick={() => setRemoving(a)}>
                          Remove
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </div>
      {removing && (
        <ConfirmDialog
          title="Remove assignment?"
          message={`${removing.doctorName} will lose access to ${removing.courseCode}.`}
          confirmLabel="Remove"
          busy={action.busy}
          onConfirm={() => void remove()}
          onCancel={() => setRemoving(null)}
        />
      )}
    </>
  );
}

// ---------------- Graduation review ----------------

export function GraduationTab() {
  const [status, setStatus] = useState('Pending');
  const [page, setPage] = useState(1);
  const apps = useApi<PagedResultDto<GraduationApplicationAdminDto>>(`/api/graduation/applications${qs({ status, page, pageSize: 20 })}`);
  const [reviewing, setReviewing] = useState<{ app: GraduationApplicationAdminDto; approve: boolean } | null>(null);
  const [note, setNote] = useState('');
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const [flash, setFlash] = useFlash();
  const action = useAction();

  const submit = () =>
    action.run(async () => {
      if (!reviewing) return;
      await api.post(`/api/graduation/applications/${reviewing.app.id}/review`, {
        approve: reviewing.approve,
        note: note.trim() || null,
        graduationYear: reviewing.approve && year ? Number(year) : null,
      });
      setFlash(reviewing.approve ? `Degree conferred on ${reviewing.app.studentName}.` : `Application from ${reviewing.app.studentName} rejected.`);
      setReviewing(null);
      setNote('');
      await apps.reload();
    });

  return (
    <div className="card">
      <div className="card-header">
        <h3>Graduation applications</h3>
        <select
          value={status}
          onChange={(e) => {
            setStatus(e.target.value);
            setPage(1);
          }}
          aria-label="Status"
        >
          <option value="">All</option>
          {['Pending', 'InReview', 'Approved', 'Rejected', 'Withdrawn'].map((s, sIdx) => (
            <option key={`${s}-${sIdx}`}>{s}</option>
          ))}
        </select>
      </div>
      <ErrorBox error={action.error} />
      {flash && <Alert kind="success">{flash}</Alert>}
      <Async res={apps} loadingLabel="Loading applications…" isEmpty={(p) => p.items.length === 0} emptyIcon="graduation-cap" empty="No applications with this status.">
        {(p) => (
          <>
            <div className="table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Student</th>
                    <th>Degree</th>
                    <th>GPA</th>
                    <th>Courses</th>
                    <th>Status</th>
                    <th>Submitted</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {p.items.map((a) => (
                    <tr key={a.id}>
                      <td>
                        {a.studentName}
                        <div className="muted small-print">{a.email}</div>
                      </td>
                      <td>{a.degree}</td>
                      <td>{a.gpaAtApplication.toFixed(2)}</td>
                      <td>
                        {a.coursesCompleted}/{a.coursesRequired}
                      </td>
                      <td>
                        <StatusBadge status={a.status === 'Approved' ? 'passed' : a.status === 'Rejected' ? 'locked' : 'in-progress'} label={a.status} />
                        {a.decisionNote && <div className="muted small-print">{a.decisionNote}</div>}
                      </td>
                      <td>{fmtDateTime(a.submittedAtUtc)}</td>
                      <td>
                        {(a.status === 'Pending' || a.status === 'InReview') && (
                          <div className="row-actions">
                            <button className="btn btn-sm btn-primary" onClick={() => setReviewing({ app: a, approve: true })}>
                              Approve
                            </button>
                            <button className="btn btn-sm btn-danger" onClick={() => setReviewing({ app: a, approve: false })}>
                              Reject
                            </button>
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination page={p.page} pageSize={p.pageSize} total={p.total} onPage={setPage} />
          </>
        )}
      </Async>
      {reviewing && (
        <Modal
          title={reviewing.approve ? `Approve ${reviewing.app.studentName}` : `Reject ${reviewing.app.studentName}`}
          onClose={() => setReviewing(null)}
          footer={
            <>
              <button className="btn btn-secondary" onClick={() => setReviewing(null)}>
                Cancel
              </button>
              <button className={`btn ${reviewing.approve ? 'btn-primary' : 'btn-danger'}`} onClick={() => void submit()} disabled={action.busy}>
                {reviewing.approve ? 'Confer degree' : 'Reject application'}
              </button>
            </>
          }
        >
          {reviewing.approve ? (
            <>
              <p>Approving confers the degree, unlocks the certificate and notifies the student.</p>
              <div className="form-group">
                <label>Graduation year</label>
                <input type="number" value={year} onChange={(e) => setYear(e.target.value)} />
              </div>
            </>
          ) : (
            <p>The student will be notified and can apply again later.</p>
          )}
          <div className="form-group">
            <label>Note (optional)</label>
            <textarea rows={3} value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} placeholder="e.g. Degree audit passed." />
          </div>
        </Modal>
      )}
    </div>
  );
}

// ---------------- Communication: notifications, announcements, events ----------------

export function CommunicationTab() {
  const universities = useApi<UniversityDto[]>('/api/auth/universities');
  const announcements = useApi<AdminAnnouncement[]>('/api/admin/announcements');
  const events = useApi<EventDto[]>('/api/events');
  const [notify, setNotify] = useState({ title: '', body: '', target: 'role', roleId: '1', userId: '' });
  const [ann, setAnn] = useState({ title: '', body: '', category: 'general', universityId: '' });
  const [evt, setEvt] = useState<{ id: number | null; title: string; location: string; startsAt: string; description: string; universityId: string } | null>(null);
  const [deletingEvent, setDeletingEvent] = useState<EventDto | null>(null);
  const [flash, setFlash] = useFlash(5000);
  const action = useAction();

  const sendNotification = (e: FormEvent) => {
    e.preventDefault();
    void action.run(async () => {
      await api.post('/api/admin/notifications', {
        title: notify.title.trim(),
        body: notify.body.trim(),
        userId: notify.target === 'user' && notify.userId ? Number(notify.userId) : null,
        roleId: notify.target === 'role' ? Number(notify.roleId) : null,
      });
      setNotify({ ...notify, title: '', body: '' });
      setFlash('Notification sent.');
    });
  };

  const postAnnouncement = (e: FormEvent) => {
    e.preventDefault();
    void action.run(async () => {
      await api.post('/api/admin/announcements', {
        title: ann.title.trim(),
        body: ann.body.trim(),
        category: ann.category.trim() || null,
        universityId: ann.universityId ? Number(ann.universityId) : null,
      });
      setAnn({ ...ann, title: '', body: '' });
      setFlash('Announcement published.');
      await announcements.reload();
    });
  };

  const saveEvent = (e: FormEvent) => {
    e.preventDefault();
    if (!evt) return;
    void action.run(async () => {
      const body = {
        title: evt.title.trim(),
        location: evt.location.trim(),
        startsAtUtc: fromDateTimeInput(evt.startsAt),
        imageUrl: null,
        description: evt.description.trim() || null,
        universityId: evt.universityId ? Number(evt.universityId) : null,
      };
      if (evt.id) await api.put(`/api/events/${evt.id}`, body);
      else await api.post('/api/events', body);
      setEvt(null);
      setFlash('Event saved.');
      await events.reload();
    });
  };

  const removeEvent = () =>
    action.run(async () => {
      if (!deletingEvent) return;
      await api.del(`/api/events/${deletingEvent.id}`);
      setDeletingEvent(null);
      await events.reload();
    });

  const uniOptions = (universities.data ?? []).map((u) => (
    <option key={u.id} value={u.id}>
      {u.name}
    </option>
  ));

  return (
    <>
      <ErrorBox error={action.error} />
      {flash && <Alert kind="success">{flash}</Alert>}
      <div className="two-column">
        <form className="card" onSubmit={sendNotification}>
          <div className="card-header">
            <h3>Send a notification</h3>
          </div>
          <div className="form-row">
            <div className="form-group">
              <label>Send to</label>
              <select value={notify.target} onChange={(e) => setNotify({ ...notify, target: e.target.value })}>
                <option value="role">Everyone with a role</option>
                <option value="user">One user (by id)</option>
              </select>
            </div>
            {notify.target === 'role' ? (
              <div className="form-group">
                <label>Role</label>
                <select value={notify.roleId} onChange={(e) => setNotify({ ...notify, roleId: e.target.value })}>
                  <option value="1">Students</option>
                  <option value="2">Graduates</option>
                  <option value="3">Doctors</option>
                  <option value="4">Admins</option>
                </select>
              </div>
            ) : (
              <div className="form-group">
                <label>User id</label>
                <input type="number" value={notify.userId} onChange={(e) => setNotify({ ...notify, userId: e.target.value })} placeholder="from the Users tab" />
              </div>
            )}
          </div>
          <div className="form-group">
            <label>Title</label>
            <input value={notify.title} onChange={(e) => setNotify({ ...notify, title: e.target.value })} maxLength={200} required />
          </div>
          <div className="form-group">
            <label>Message</label>
            <textarea rows={3} value={notify.body} onChange={(e) => setNotify({ ...notify, body: e.target.value })} maxLength={2000} required />
          </div>
          <button className="btn btn-primary" type="submit" disabled={action.busy || !notify.title.trim() || !notify.body.trim() || (notify.target === 'user' && !notify.userId)}>
            Send
          </button>
        </form>

        <form className="card" onSubmit={postAnnouncement}>
          <div className="card-header">
            <h3>Post an announcement</h3>
          </div>
          <div className="form-row">
            <div className="form-group">
              <label>University</label>
              <select value={ann.universityId} onChange={(e) => setAnn({ ...ann, universityId: e.target.value })}>
                <option value="">All universities</option>
                {uniOptions}
              </select>
            </div>
            <div className="form-group">
              <label>Category</label>
              <input value={ann.category} onChange={(e) => setAnn({ ...ann, category: e.target.value })} maxLength={50} />
            </div>
          </div>
          <div className="form-group">
            <label>Title</label>
            <input value={ann.title} onChange={(e) => setAnn({ ...ann, title: e.target.value })} maxLength={200} required />
          </div>
          <div className="form-group">
            <label>Body</label>
            <textarea rows={3} value={ann.body} onChange={(e) => setAnn({ ...ann, body: e.target.value })} maxLength={5000} required />
          </div>
          <button className="btn btn-primary" type="submit" disabled={action.busy || !ann.title.trim() || !ann.body.trim()}>
            Publish
          </button>
        </form>
      </div>

      <div className="card section-gap-top">
        <div className="card-header">
          <h3>Events</h3>
          <button
            className="btn btn-primary btn-sm"
            onClick={() => setEvt({ id: null, title: '', location: '', startsAt: '', description: '', universityId: String(universities.data?.[0]?.id ?? '') })}
          >
            New event
          </button>
        </div>
        <Async res={events} loadingLabel="Loading events…" emptyIcon="calendar-days" empty="No events yet.">
          {(list) => (
            <div className="table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Event</th>
                    <th>When</th>
                    <th>Where</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {[...list]
                    .sort((a, b) => b.startsAtUtc.localeCompare(a.startsAtUtc))
                    .map((ev) => (
                      <tr key={ev.id}>
                        <td>
                          <strong>{ev.title}</strong>
                          {ev.description && <div className="muted small-print">{ev.description}</div>}
                        </td>
                        <td>{fmtDateTime(ev.startsAtUtc)}</td>
                        <td>{ev.location}</td>
                        <td>
                          <div className="row-actions">
                            <button
                              className="btn btn-sm btn-secondary"
                              onClick={() =>
                                setEvt({ id: ev.id, title: ev.title, location: ev.location, startsAt: toDateTimeInput(ev.startsAtUtc), description: ev.description ?? '', universityId: '' })
                              }
                            >
                              Edit
                            </button>
                            <button className="btn btn-sm btn-danger" onClick={() => setDeletingEvent(ev)}>
                              Delete
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </div>

      <div className="card section-gap-top">
        <div className="card-header">
          <h3>Recent announcements</h3>
        </div>
        <Async res={announcements} loadingLabel="Loading announcements…" emptyIcon="megaphone" empty="No announcements yet.">
          {(list) => (
            <div className="announcement-list">
              {list.slice(0, 10).map((a) => (
                <div className="announcement-item" key={a.id}>
                  <div className="announcement-header">
                    <h4>{a.title}</h4>
                    <span className="announcement-category">{a.category ?? 'general'}</span>
                  </div>
                  <p>{a.body}</p>
                  <span className="announcement-date">{fmtDateTime(a.createdAtUtc)}</span>
                </div>
              ))}
            </div>
          )}
        </Async>
      </div>

      {evt && (
        <Modal title={evt.id ? 'Edit event' : 'New event'} onClose={() => setEvt(null)}>
          <form onSubmit={saveEvent}>
            <div className="form-group">
              <label>Title</label>
              <input value={evt.title} onChange={(e) => setEvt({ ...evt, title: e.target.value })} required />
            </div>
            <div className="form-row">
              <div className="form-group">
                <label>Starts at</label>
                <input type="datetime-local" value={evt.startsAt} onChange={(e) => setEvt({ ...evt, startsAt: e.target.value })} required />
              </div>
              <div className="form-group">
                <label>Location</label>
                <input value={evt.location} onChange={(e) => setEvt({ ...evt, location: e.target.value })} required />
              </div>
            </div>
            <div className="form-group">
              <label>University</label>
              <select value={evt.universityId} onChange={(e) => setEvt({ ...evt, universityId: e.target.value })} required>
                <option value="">Select a university…</option>
                {uniOptions}
              </select>
              {evt.id && <span className="field-hint">The API does not return an event's university, so choose it again when editing.</span>}
            </div>
            <div className="form-group">
              <label>Description</label>
              <textarea rows={3} value={evt.description} onChange={(e) => setEvt({ ...evt, description: e.target.value })} />
            </div>
            <div className="modal-actions flush">
              <button type="button" className="btn btn-secondary" onClick={() => setEvt(null)}>
                Cancel
              </button>
              <button type="submit" className="btn btn-primary" disabled={action.busy}>
                Save event
              </button>
            </div>
          </form>
        </Modal>
      )}
      {deletingEvent && (
        <ConfirmDialog title="Delete event?" message={`Delete “${deletingEvent.title}”?`} busy={action.busy} onConfirm={() => void removeEvent()} onCancel={() => setDeletingEvent(null)} />
      )}
    </>
  );
}

// ---------------- Risk, departments, audit, system ----------------

export function RiskTab() {
  const res = useApi<AtRiskStudentDto[]>('/api/admin/analytics/at-risk-students');
  return (
    <div className="card">
      <div className="card-header">
        <h3>Students at risk</h3>
      </div>
      <Async res={res} loadingLabel="Scoring students…" emptyIcon="circle-check" empty="No at-risk students right now.">
        {(list) => (
          <div className="table-wrapper">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Email</th>
                  <th>Department</th>
                  <th>Risk</th>
                  <th>Score</th>
                  <th>Predicted GPA</th>
                </tr>
              </thead>
              <tbody>
                {list.map((s) => (
                  <tr key={s.id}>
                    <td>{s.fullName}</td>
                    <td>{s.email}</td>
                    <td>{s.department}</td>
                    <td>
                      <StatusBadge status={s.riskLevel === 'AtRisk' ? 'needs-attention' : 'good'} label={s.riskLevel === 'AtRisk' ? 'At risk' : s.riskLevel === 'NeedsAttention' ? 'Needs attention' : s.riskLevel} />
                    </td>
                    <td>{Math.round(s.score)}</td>
                    <td>{s.predictedGpa.toFixed(2)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Async>
    </div>
  );
}

export function DepartmentsTab() {
  const res = useApi<DepartmentStatsDto[]>('/api/admin/analytics/departments');
  return (
    <div className="card">
      <div className="card-header">
        <h3>Departments</h3>
      </div>
      <Async res={res} loadingLabel="Loading departments…" empty="No department data yet.">
        {(list) => (
          <div className="table-wrapper">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Department</th>
                  <th>Students</th>
                  <th>Avg GPA</th>
                  <th>Avg attendance</th>
                </tr>
              </thead>
              <tbody>
                {list.map((d, dIdx) => (
                  <tr key={`${d.department}-${dIdx}`}>
                    <td>{d.department}</td>
                    <td>{d.students}</td>
                    <td>{d.averageGpa.toFixed(2)}</td>
                    <td>{Math.round(d.averageAttendance)}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Async>
    </div>
  );
}

export function AuditTab() {
  const [page, setPage] = useState(1);
  const res = useApi<PagedResultDto<AuditLogDto>>(`/api/admin/audit-log?page=${page}&pageSize=50`);
  return (
    <div className="card">
      <div className="card-header">
        <h3>Audit log</h3>
        <button className="btn btn-sm btn-secondary" onClick={() => void res.reload()}>
          Refresh
        </button>
      </div>
      <Async res={res} loadingLabel="Loading audit log…" isEmpty={(p) => p.items.length === 0} empty="No audit entries yet.">
        {(p) => (
          <>
            <div className="table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Time</th>
                    <th>User</th>
                    <th>Method</th>
                    <th>Path</th>
                    <th>Status</th>
                    <th>ms</th>
                    <th>IP</th>
                  </tr>
                </thead>
                <tbody>
                  {p.items.map((a) => (
                    <tr key={a.id}>
                      <td>{fmtDateTime(a.timestamp)}</td>
                      <td>{a.userId}</td>
                      <td>{a.method}</td>
                      <td className="mono">{a.path}</td>
                      <td>
                        <span className={`status-badge ${a.statusCode >= 500 ? 'needs-attention' : a.statusCode >= 400 ? 'in-progress' : 'excellent'}`}>{a.statusCode}</span>
                      </td>
                      <td>{a.durationMs}</td>
                      <td className="muted">{a.ipAddress}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination page={p.page} pageSize={p.pageSize} total={p.total} onPage={setPage} />
          </>
        )}
      </Async>
    </div>
  );
}

function uptime(seconds: number): string {
  const d = Math.floor(seconds / 86400);
  const h = Math.floor((seconds % 86400) / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  return d > 0 ? `${d}d ${h}h` : h > 0 ? `${h}h ${m}m` : `${m}m`;
}

export function SystemTab() {
  const res = useApi<SystemHealthDto>('/api/admin/system-health');
  return (
    <Async res={res} loadingLabel="Checking system health…">
      {(h) => (
        <>
          <div className="summary-cards admin-kpis">
            {(
              [
                ['Status', h.status],
                ['Database', h.database],
                ['Uptime', uptime(h.uptimeSeconds)],
                ['Version', h.version],
              ] as Array<[string, string]>
            ).map(([l, v]) => (
              <div className="card" key={l}>
                <div className="card-info">
                  <span className="card-label">{l}</span>
                  <span className="card-value card-value-small">{v}</span>
                </div>
              </div>
            ))}
          </div>
          <p className="muted">Server time: {fmtDateTime(h.serverTimeUtc)}</p>
        </>
      )}
    </Async>
  );
}

export { Empty };
