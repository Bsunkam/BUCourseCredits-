/** Fired when the API answers 401: the session expired, was ended, or the account was disabled. */
export const SIGNED_OUT_EVENT = "tc:signed-out";

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

// The session is an HttpOnly cookie set by /api/auth/login; fetch sends it on same-origin requests.
async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {};
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
    // /api/me answers 401 to every visitor who is not signed in yet; that is not a session ending.
    if (res.status === 401 && !path.startsWith("/api/auth/") && path !== "/api/me") {
      message = "Your session has ended. Sign in again.";
      window.dispatchEvent(new Event(SIGNED_OUT_EVENT));
    }
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
    const res = await fetch(link.url);
    if (!res.ok) throw new ApiError(res.status, "Could not open the document.");
    const blob = await res.blob();
    window.open(URL.createObjectURL(blob), "_blank", "noopener");
  },
};
