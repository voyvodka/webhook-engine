export interface AuthUser {
  id: string;
  email: string;
  role: string;
  lastLoginAt?: string | null;
}

interface Envelope<T> {
  data: T;
}

async function parseError(response: Response): Promise<string> {
  try {
    const payload = (await response.json()) as {
      error?: { message?: string };
    };
    if (payload.error?.message) {
      return payload.error.message;
    }
  } catch {
    // no-op
  }

  return `Request failed with status ${response.status}`;
}

export async function login(email: string, password: string): Promise<AuthUser> {
  const response = await fetch("/api/v1/auth/login", {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify({ email, password })
  });

  if (!response.ok) {
    throw new Error(await parseError(response));
  }

  const payload = (await response.json()) as Envelope<AuthUser>;
  return payload.data;
}

export async function logout(): Promise<void> {
  await fetch("/api/v1/auth/logout", {
    method: "POST",
    credentials: "include"
  });
}

export async function getCurrentUser(): Promise<AuthUser | null> {
  const response = await fetch("/api/v1/auth/me", {
    method: "GET",
    credentials: "include"
  });

  if (response.status === 401) {
    return null;
  }

  if (!response.ok) {
    throw new Error(await parseError(response));
  }

  const payload = (await response.json()) as Envelope<AuthUser>;
  return payload.data;
}
