// ============================================================
//  api.js - החיבור בין ה-client לשרת ה-C# (ASP.NET Core)
//  ------------------------------------------------------------
//  במקום Firebase, כל פעולה נשלחת כבקשת HTTP לשרת שלך.
//  השרת בודק את המשתמש מול מסד הנתונים ומחזיר תשובה ב-JSON.
//  ההתחברות נשמרת ב-cookie שהשרת יוצר (credentials: "include").
// ============================================================

// הכתובת של שרת ה-C# (משנים כאן אם הפורט שונה).
// אותו שם מחשב כמו הדף (localhost או 127.0.0.1), אחרת הדפדפן לא ישלח את ה-cookie
export const API_BASE = "http://" + (window.location.hostname || "localhost") + ":5000/api";

async function request(path, options = {}) {
  let response;
  try {
    response = await fetch(API_BASE + path, {
      method: options.method || "GET",
      credentials: "include",
      headers: options.body ? { "Content-Type": "application/json" } : undefined,
      body: options.body ? JSON.stringify(options.body) : undefined
    });
  } catch {
    throw new Error("אין חיבור לשרת. ודא ששרת ה-C# פועל");
  }

  const data = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error((data && data.message) || "אירעה שגיאה. נסה שוב");
  }
  return data;
}

// POST /api/auth/register  { fullName, email, password, newsletter }
export function register(fullName, email, password, newsletter) {
  return request("/auth/register", { method: "POST", body: { fullName, email, password, newsletter } });
}

// POST /api/auth/login  { email, password, rememberMe }
export function login(email, password, rememberMe) {
  return request("/auth/login", { method: "POST", body: { email, password, rememberMe } });
}

// POST /api/auth/logout
export function logout() {
  return request("/auth/logout", { method: "POST" });
}

// GET /api/auth/me  -> המשתמש המחובר, או שגיאה 401 אם אין
export async function getCurrentUser() {
  try {
    return await request("/auth/me");
  } catch {
    return null;
  }
}

// POST /api/auth/forgot-password  { email }
export function forgotPassword(email) {
  return request("/auth/forgot-password", { method: "POST", body: { email } });
}

// ---------- קבצים ----------

// העלאת קובץ. הקובץ נשמר בשרת תחת תיקייה ששמה הוא ה-UserId של המחובר
export async function uploadFile(file) {
  const formData = new FormData();
  formData.append("file", file);

  let response;
  try {
    // בלי Content-Type ידני: הדפדפן מוסיף אותו לבד עם ה-boundary הנכון
    response = await fetch(API_BASE + "/files", {
      method: "POST",
      credentials: "include",
      body: formData
    });
  } catch {
    throw new Error("אין חיבור לשרת. ודא ששרת ה-C# פועל");
  }

  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error((data && data.message) || "ההעלאה נכשלה");
  return data;
}

// רשימת הקבצים של המשתמש המחובר בלבד
export function listFiles() {
  return request("/files");
}

export function deleteFile(id) {
  return request("/files/" + id, { method: "DELETE" });
}

// כתובת ההורדה. הדפדפן שולח את העוגייה, והשרת בודק בעלות לפני שהוא מגיש
export function fileDownloadUrl(id) {
  return API_BASE + "/files/" + id;
}

// התחברות עם Google: השרת מפנה לגוגל, ואחרי האישור מחזיר לכתובת returnUrl
export function googleLogin(returnUrl) {
  window.location.href = API_BASE + "/auth/google?returnUrl=" + encodeURIComponent(returnUrl);
}
