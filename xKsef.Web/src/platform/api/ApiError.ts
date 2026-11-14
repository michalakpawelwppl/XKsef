export class ApiError extends Error {
  constructor(readonly status: number, message: string, readonly validation: readonly string[] = [], readonly retryAfterSeconds: number | null = null) {
    super(message); this.name = 'ApiError';
  }
}
export function statusMessage(status: number): string {
  switch (status) {
    case 400: return 'Sprawdź poprawność wprowadzonych danych.';
    case 401: return 'Sesja wygasła. Zaloguj się ponownie.';
    case 403: return 'Brak uprawnień do wykonania tej operacji.';
    case 429: return 'Zbyt wiele żądań. Odczekaj przed kolejną próbą.';
    case 503: return 'Usługa jest chwilowo niedostępna. Spróbuj ponownie później.';
    case 0: return 'Nie udało się połączyć z API. Sprawdź połączenie i spróbuj ponownie.';
    default: return 'Nie udało się wykonać operacji. Spróbuj ponownie później.';
  }
}
export function errorMessage(error: unknown) { return error instanceof ApiError ? error.message : 'Nie udało się wykonać operacji.'; }
export function retryAfterSeconds(value: string | null, now = Date.now()): number | null {
  if (!value) return null;
  if (/^\d+$/.test(value)) return Number(value);
  const date = Date.parse(value);
  return Number.isFinite(date) ? Math.max(0, Math.ceil((date - now) / 1000)) : null;
}
