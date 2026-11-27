import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { UiIcon } from "../UiIcon";
import { UserAvatar } from "../components/UserAvatar";
import { Alert, Button, Card, CardHeader, LoadingBlock } from "../components/ui";
import { API_BASE_URL, authHeaders, errorMessage, notifyUserUpdated, type AuthUser } from "../lib/api";

export function Profile() {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [photoBusy, setPhotoBusy] = useState(false);
  const [toast, setToast] = useState<{ kind: "success" | "error"; text: string } | null>(null);

  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [major, setMajor] = useState("");

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [pwBusy, setPwBusy] = useState(false);

  const fileInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    document.title = "UniSphere 360+ — My Profile";
    void (async () => {
      try {
        const res = await fetch(`${API_BASE_URL}/api/auth/me`, { headers: authHeaders() });
        if (res.ok) {
          const data = (await res.json()) as AuthUser;
          setUser(data);
          setFirstName(data.firstName ?? data.fullName.split(" ")[0] ?? "");
          setLastName(data.lastName ?? data.fullName.split(" ").slice(1).join(" ") ?? "");
          setMajor(data.major ?? "");
        }
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  async function handlePhotoSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    if (!file.type.startsWith("image/")) {
      setToast({ kind: "error", text: "Please choose an image file (PNG, JPG, or WebP)." });
      return;
    }
    if (file.size > 5 * 1024 * 1024) {
      setToast({ kind: "error", text: "Image must be smaller than 5 MB." });
      return;
    }

    setPhotoBusy(true);
    setToast(null);
    try {
      const form = new FormData();
      form.append("file", file);
      const res = await fetch(`${API_BASE_URL}/api/users/${user!.id}/avatar`, {
        method: "POST",
        headers: authHeaders(),
        body: form,
      });
      if (!res.ok) throw new Error(await errorMessage(res, "Failed to upload photo."));
      const updated = { ...user!, ...((await res.json()) as Partial<AuthUser>) };
      setUser(updated);
      notifyUserUpdated(updated);
      setToast({ kind: "success", text: "Profile photo updated and synced across your workspace." });
    } catch (e) {
      setToast({ kind: "error", text: e instanceof Error ? e.message : "Could not upload photo." });
    } finally {
      setPhotoBusy(false);
    }
  }

  async function handleRemovePhoto() {
    setPhotoBusy(true);
    setToast(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/users/${user!.id}/avatar`, {
        method: "DELETE",
        headers: authHeaders(),
      });
      if (!res.ok) throw new Error("Failed to remove photo.");
      const updated = { ...user!, ...((await res.json()) as Partial<AuthUser>) };
      setUser(updated);
      notifyUserUpdated(updated);
      setToast({ kind: "success", text: "Profile photo removed." });
    } catch {
      setToast({ kind: "error", text: "Could not remove photo. Please try again." });
    } finally {
      setPhotoBusy(false);
    }
  }

  async function handleSaveProfile(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setToast(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/users/${user!.id}`, {
        method: "PUT",
        headers: { ...authHeaders(), "Content-Type": "application/json" },
        body: JSON.stringify({
          fullName: `${firstName} ${lastName}`.trim(),
          major,
          avatarUrl: user!.avatarUrl ?? null,
        }),
      });
      if (!res.ok) throw new Error(await errorMessage(res, "Failed to save profile changes."));
      const updated = { ...user!, ...((await res.json()) as Partial<AuthUser>) };
      setUser(updated);
      notifyUserUpdated(updated);
      setToast({ kind: "success", text: "Profile changes saved." });
    } catch (e) {
      setToast({ kind: "error", text: e instanceof Error ? e.message : "Failed to save profile changes." });
    } finally {
      setSaving(false);
    }
  }

  async function handleChangePassword(event: FormEvent) {
    event.preventDefault();
    setPwBusy(true);
    setToast(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/auth/change-password`, {
        method: "POST",
        headers: { ...authHeaders(), "Content-Type": "application/json" },
        body: JSON.stringify({ currentPassword, newPassword }),
      });
      if (!res.ok) throw new Error(await errorMessage(res, "Could not update password."));
      setCurrentPassword("");
      setNewPassword("");
      setToast({ kind: "success", text: "Password updated successfully." });
    } catch (e) {
      setToast({ kind: "error", text: e instanceof Error ? e.message : "Could not update password." });
    } finally {
      setPwBusy(false);
    }
  }

  if (loading || !user) return <LoadingBlock label="Loading your profile…" />;

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="m-0 text-2xl font-bold tracking-tight text-text">My Profile</h1>
        <p className="mt-1 mb-0 text-sm text-text-secondary">
          Manage your circular avatar, personal details, and account security across all UniSphere views.
        </p>
      </div>

      {toast && <Alert kind={toast.kind}>{toast.text}</Alert>}

      {/* Circular Photo Card (All 4 Roles) */}
      <Card>
        <CardHeader title="Profile Photo" />
        <div className="flex flex-wrap items-center gap-6 px-5 py-5">
          <UserAvatar fullName={user.fullName} avatarUrl={user.avatarUrl} size={92} className="ring-4 ring-primary/20" />
          <div className="min-w-[240px] flex-1">
            <div className="text-lg font-bold text-text">{user.fullName}</div>
            <div className="mt-0.5 text-sm text-text-secondary">
              {user.role} · {user.email}
              {user.universityName ? ` · ${user.universityName}` : ""}
            </div>
            <p className="mt-2 mb-3 text-xs text-text-muted">
              Photos are displayed as crisp circles in your Profile, Sidebar, and Topbar badge. Upload PNG, JPG, or WebP (up to 5 MB).
            </p>
            <div className="flex flex-wrap items-center gap-2.5">
              <input
                ref={fileInputRef}
                type="file"
                accept="image/png,image/jpeg,image/webp"
                onChange={(e) => void handlePhotoSelected(e)}
                className="hidden"
              />
              <Button size="sm" onClick={() => fileInputRef.current?.click()} disabled={photoBusy}>
                <UiIcon name="camera" /> {photoBusy ? "Updating…" : user.avatarUrl ? "Change Photo" : "Upload Photo"}
              </Button>
              {user.avatarUrl && (
                <Button size="sm" variant="secondary" onClick={() => void handleRemovePhoto()} disabled={photoBusy}>
                  <UiIcon name="trash-2" /> Remove Photo
                </Button>
              )}
            </div>
          </div>
        </div>
      </Card>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        {/* Editable Profile Form */}
        <Card className="lg:col-span-2">
          <CardHeader title="Account Information" />
          <form onSubmit={(e) => void handleSaveProfile(e)} className="grid grid-cols-1 gap-4 px-5 py-5 sm:grid-cols-2">
            <label className="flex flex-col gap-1.5 text-xs font-semibold text-text-secondary">
              First Name
              <input
                value={firstName}
                onChange={(e) => setFirstName(e.target.value)}
                className="rounded-control border border-border bg-bg px-3.5 py-2.5 text-sm text-text outline-none focus:border-primary"
              />
            </label>
            <label className="flex flex-col gap-1.5 text-xs font-semibold text-text-secondary">
              Last Name
              <input
                value={lastName}
                onChange={(e) => setLastName(e.target.value)}
                className="rounded-control border border-border bg-bg px-3.5 py-2.5 text-sm text-text outline-none focus:border-primary"
              />
            </label>
            <label className="flex flex-col gap-1.5 text-xs font-semibold text-text-secondary sm:col-span-2">
              Major / Focus Area
              <input
                value={major}
                onChange={(e) => setMajor(e.target.value)}
                className="rounded-control border border-border bg-bg px-3.5 py-2.5 text-sm text-text outline-none focus:border-primary"
              />
            </label>
            <div className="sm:col-span-2">
              <Button type="submit" disabled={saving}>
                {saving ? "Saving…" : "Save Changes"}
              </Button>
            </div>
          </form>
        </Card>

        {/* Security / Change Password */}
        <Card>
          <CardHeader title="Security" />
          <form onSubmit={(e) => void handleChangePassword(e)} className="flex flex-col gap-4 px-5 py-5">
            <label className="flex flex-col gap-1.5 text-xs font-semibold text-text-secondary">
              Current Password
              <input
                type="password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                className="rounded-control border border-border bg-bg px-3.5 py-2.5 text-sm text-text outline-none focus:border-primary"
              />
            </label>
            <label className="flex flex-col gap-1.5 text-xs font-semibold text-text-secondary">
              New Password (min 8 chars)
              <input
                type="password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                className="rounded-control border border-border bg-bg px-3.5 py-2.5 text-sm text-text outline-none focus:border-primary"
              />
            </label>
            <Button type="submit" variant="secondary" disabled={pwBusy || !currentPassword || !newPassword}>
              {pwBusy ? "Updating…" : "Update Password"}
            </Button>
          </form>
        </Card>
      </div>
    </div>
  );
}
