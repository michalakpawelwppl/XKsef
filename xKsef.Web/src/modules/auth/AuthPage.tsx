import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { useSession } from '../../platform/session/SessionProvider';

export function AuthPage({ register = false }: { register?: boolean }) {
  const { store, state } = useSession(); const navigate = useNavigate();
  const [login, setLogin] = useState(''); const [email, setEmail] = useState('');
  const [password, setPassword] = useState(''); const [repeat, setRepeat] = useState('');
  const [localError, setLocalError] = useState<string | null>(null);
  const form = useRef<HTMLFormElement>(null);
  useEffect(() => () => { form.current?.querySelectorAll<HTMLInputElement>('input[type=password]').forEach(input => { input.value = ''; }); }, []);
  async function submit(event: React.SubmitEvent<HTMLFormElement>) {
    event.preventDefault(); setLocalError(null);
    if (register && password !== repeat) { setLocalError('Hasła muszą być identyczne.'); return; }
    const currentForm = form.current;
    try {
      const ok = register ? await store.register(login.trim(), email.trim(), password) : await store.login(login.trim(), password);
      if (ok && register) void navigate('/login', { replace: true });
    } finally {
      setPassword(''); setRepeat('');
      currentForm?.querySelectorAll<HTMLInputElement>('input[type=password]').forEach(input => { input.value = ''; });
    }
  }
  return <main className="auth-page"><section className="auth-card" aria-labelledby="auth-title">
    <Link className="brand" to="/login"><span className="brand-mark">K</span>xKsef</Link>
    <p className="eyebrow">Finanse i księgowość</p>
    <h1 id="auth-title">{register ? 'Utwórz konto' : 'Zaloguj się'}</h1>
    <p>{register ? 'Zarejestruj prywatne konto. Dostęp do firm otrzymasz osobno.' : 'Wybierz firmę po zalogowaniu.'}</p>
    {state.notice && <p className="notice" role="status">{state.notice}</p>}
    {(localError || state.error) && <div className="error-message" role="alert"><p>{localError ?? state.error}</p>
      {!localError && state.validation.length > 0 && <ul>{state.validation.map((message, index) => <li key={index}>{message}</li>)}</ul>}</div>}
    <form ref={form} onSubmit={event => { void submit(event); }}>
      <label>Login<input name="login" autoComplete="username" maxLength={256} value={login} onChange={event => setLogin(event.target.value)} required disabled={state.busy} /></label>
      {register && <label>E-mail<input name="email" type="email" autoComplete="email" maxLength={256} value={email} onChange={event => setEmail(event.target.value)} required disabled={state.busy} /></label>}
      <label>Hasło<input name="password" type="password" autoComplete={register ? 'new-password' : 'current-password'} maxLength={256} value={password} onChange={event => setPassword(event.target.value)} required disabled={state.busy} /></label>
      {register && <><label>Powtórz hasło<input name="repeat" type="password" autoComplete="new-password" maxLength={256} value={repeat} onChange={event => setRepeat(event.target.value)} required disabled={state.busy} /></label>
        <p className="form-hint">Co najmniej 12 znaków, mała i wielka litera, cyfra oraz znak specjalny.</p></>}
      <button className="primary-button" disabled={state.busy} type="submit">{state.busy ? 'Proszę czekać…' : register ? 'Zarejestruj' : 'Zaloguj'}</button>
    </form>
    <p>{register ? 'Masz już konto? ' : 'Nie masz konta? '}<Link to={register ? '/login' : '/register'} aria-disabled={state.busy} onClick={event => { if (state.busy) event.preventDefault(); }}>{register ? 'Zaloguj się' : 'Utwórz nowego użytkownika'}</Link></p>
  </section></main>;
}
