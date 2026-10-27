const USER_KEY = "tc.demoUser";

export function getDemoUser(): string | null {
  try {
    return localStorage.getItem(USER_KEY);
  } catch {
    return null;
  }
}

export function setDemoUser(id: string | null) {
  try {
    if (id) localStorage.setItem(USER_KEY, id);
    else localStorage.removeItem(USER_KEY);
  } catch {
    /* storage unavailable: the session simply won't persist */
  }
}

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {};
  const user = getDemoUser();
  if (user) headers["X-Demo-User"] = user;
  let payload: BodyInit | undefined;
  if (body instanceof FormData) payload = body;
  else if (body !== undefined) {
    headers["Content-Type"] = "application/json";
    payload = JSON.stringify(body);
  }

  let res: Response;
  try {
    res = await fetch(path, { method, headers, body: payload });
  } catch {
    throw new ApiError(0, "Cannot reach the API. Check that it is running on http://localhost:5090.");
  }
  // A web page instead of data means the request never reached the API (it is down, or the UI is served without the /api proxy).
  if ((res.headers.get("content-type") ?? "").includes("text/html")) {
    console.error(`Expected JSON from ${method} ${path}, got a web page (HTTP ${res.status}, ${res.url}).`);
    throw new ApiError(res.status, `The API did not answer ${method} ${path} (HTTP ${res.status}). Check that it is running on http://localhost:5090 and open the app at http://localhost:5190.`);
  }
  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`;
    try {
      const problem = await res.json();
      message = problem.detail || problem.title || message;
    } catch {
      /* not JSON */
    }
    if (res.status === 403 && message.startsWith("403")) message = "You do not have access to this.";
    throw new ApiError(res.status, message);
  }
  if (res.status === 204 || res.status === 202) {
    const text = await res.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }
  return (await res.json()) as T;
}

export const api = {
  get: <T>(path: string) => request<T>("GET", path),
  post: <T>(path: string, body?: unknown) => request<T>("POST", path, body),
  put: <T>(path: string, body?: unknown) => request<T>("PUT", path, body),

  /** Opens a clean document: a SAS link in Azure, or an authenticated download from the API locally. */
  async openDocument(id: number) {
    const link = await request<{ url: string; kind: "sas" | "api" }>("GET", `/api/documents/${id}/link`);
    if (link.kind === "sas") {
      window.open(link.url, "_blank", "noopener");
      return;
    }
    const user = getDemoUser();
    const res = await fetch(link.url, { headers: user ? { "X-Demo-User": user } : {} });
    if (!res.ok) throw new ApiError(res.status, "Could not open the document.");
    const blob = await res.blob();
    window.open(URL.createObjectURL(blob), "_blank", "noopener");
  },
};
