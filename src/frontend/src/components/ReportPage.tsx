import React, { useState, useEffect } from "react";

interface UserInfo {
  preferredUsername: string;
  email: string;
  name: string;
}

interface ReportData {
  message?: string;
  data?: any;
  report?: string;
}

const ReportPage: React.FC = () => {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [authenticated, setAuthenticated] = useState(false);
  const [userInfo, setUserInfo] = useState<UserInfo | null>(null);
  const [reportData, setReportData] = useState<ReportData | null>(null);

  // API base URL - используем переменную окружения или localhost для Docker
  const apiUrl = process.env.REACT_APP_API_URL || "http://localhost:8000";

  // Проверка сессии при загрузке
  useEffect(() => {
    checkSession();
  }, []);

  const checkSession = async () => {
    try {
      const response = await fetch(`${apiUrl}/api/auth/validate`, {
        credentials: "include",
      });
      const data = await response.json();
      setAuthenticated(data.authenticated);

      if (data.authenticated) {
        await getUserInfo();
      }
    } catch (err) {
      console.error("Session check failed:", err);
      setAuthenticated(false);
    }
  };

  const getUserInfo = async () => {
    try {
      const response = await fetch(`${apiUrl}/api/auth/userinfo`, {
        credentials: "include",
      });
      if (response.ok) {
        const data = await response.json();
        setUserInfo(data);
      }
    } catch (err) {
      console.error("Failed to get user info:", err);
    }
  };

  const login = async (username: string, password: string) => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetch(`${apiUrl}/api/auth/login`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        credentials: "include",
        body: JSON.stringify({ username, password }),
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || "Login failed");
      }

      const data = await response.json();
      console.log("Login successful:", data);

      await new Promise(resolve => setTimeout(resolve, 500));
      setAuthenticated(true);
      await getUserInfo();

    } catch (err) {
      setError(err instanceof Error ? err.message : "Login failed");
    } finally {
      setLoading(false);
    }
  };

  const refreshToken = async () => {
    try {
      const response = await fetch(`${apiUrl}/api/auth/refresh`, {
        method: "POST",
        credentials: "include",
      });

      if (!response.ok) {
        throw new Error("Refresh failed");
      }

      const data = await response.json();
      console.log("Token refreshed successfully");
      return true;
    } catch (err) {
      console.error("Refresh failed:", err);

      await new Promise(resolve => setTimeout(resolve, 100));
      setAuthenticated(false);
      setUserInfo(null);
      return false;
    }
  };

  const logout = async () => {
    try {
      await fetch(`${apiUrl}/api/auth/logout`, {
        method: "POST",
        credentials: "include",
      });
      setAuthenticated(false);
      setUserInfo(null);
      setReportData(null);
    } catch (err) {
      console.error("Logout failed:", err);
    }
  };

  const downloadReport = async () => {
    if (!authenticated) {
      setError("Not authenticated");
      return;
    }

    try {
      setLoading(true);
      setError(null);
      setReportData(null);

      let response = await fetch(`${apiUrl}/api/reports`, {
        credentials: "include",
        headers: {
          "Content-Type": "application/json",
          "X-Session-Rotate": "true" 
        },
      });

      if (response.status === 401) {
        console.log("Token expired, refreshing...");
        const refreshed = await refreshToken();

        if (refreshed) {
          // Повторяем запрос после обновления токена
          response = await fetch(`${apiUrl}/api/reports`, {
            credentials: "include",
            headers: {
              "Content-Type": "application/json",
            },
          });
        }
      }

      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(
          errorData.error || `HTTP ${response.status}: ${response.statusText}`,
        );
      }

      const data = await response.json();
      setReportData(data);

      // Показываем отчет в alert (или можно отобразить в модальном окне)
      alert(JSON.stringify(data, null, 2));
    } catch (err) {
      setError(err instanceof Error ? err.message : "An error occurred");
      console.error("Download report error:", err);
    } finally {
      setLoading(false);
    }
  };

  if (!authenticated) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen bg-gray-100">
        <div className="p-8 bg-white rounded-lg shadow-md w-96">
          <h1 className="text-2xl font-bold mb-6 text-center">Login</h1>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              const formData = new FormData(e.currentTarget);
              login(
                formData.get("username") as string,
                formData.get("password") as string,
              );
            }}
          >
            <input
              name="username"
              type="text"
              placeholder="Username"
              className="w-full mb-4 p-2 border rounded focus:outline-none focus:ring-2 focus:ring-blue-500"
              required
            />
            <input
              name="password"
              type="password"
              placeholder="Password"
              className="w-full mb-4 p-2 border rounded focus:outline-none focus:ring-2 focus:ring-blue-500"
              required
            />
            <button
              type="submit"
              disabled={loading}
              className="w-full px-4 py-2 bg-blue-500 text-white rounded hover:bg-blue-600 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {loading ? "Loading..." : "Sign In"}
            </button>
          </form>
          {error && (
            <div className="mt-4 p-3 bg-red-100 text-red-700 rounded border border-red-200">
              {error}
            </div>
          )}
          <div className="mt-4 text-sm text-gray-500 text-center">
            <p>Test credentials:</p>
            <p>user1 / password123</p>
            <p>prothetic3 / prothetic123</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-gray-100">
      <div className="p-8 bg-white rounded-lg shadow-md w-full max-w-2xl">
        <h1 className="text-2xl font-bold mb-6">Usage Reports</h1>

        {userInfo && (
          <div className="mb-4 p-4 bg-blue-50 rounded border border-blue-200">
            <p className="font-semibold">
              Welcome, {userInfo.name || userInfo.preferredUsername}!
            </p>
            <p className="text-sm text-gray-600">{userInfo.email}</p>
          </div>
        )}

        <div className="flex gap-2 mb-4">
          <button
            onClick={downloadReport}
            disabled={loading}
            className="px-4 py-2 bg-blue-500 text-white rounded hover:bg-blue-600 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {loading ? "Generating Report..." : "Download Report"}
          </button>

          <button
            onClick={logout}
            className="px-4 py-2 bg-gray-500 text-white rounded hover:bg-gray-600 transition-colors"
          >
            Logout
          </button>
        </div>

        {reportData && (
          <div className="mt-4 p-4 bg-gray-50 rounded border border-gray-200">
            <h3 className="font-semibold mb-2">Report Result:</h3>
            <pre className="text-sm overflow-auto">
              {JSON.stringify(reportData, null, 2)}
            </pre>
          </div>
        )}

        {error && (
          <div className="mt-4 p-3 bg-red-100 text-red-700 rounded border border-red-200">
            {error}
          </div>
        )}
      </div>
    </div>
  );
};

export default ReportPage;
