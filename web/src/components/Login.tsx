import { loginUrl } from '../api'

export function Login({ error }: { error: string | null }) {
  return (
    <div className="login">
      <img src="/icon.svg" alt="" width={72} height={72} />
      <h1>GoogleSlacking</h1>
      <p>Brži klijent za Google Chat.</p>
      {error && <p className="error">{error}</p>}
      <a className="btn primary" href={loginUrl('/')}>
        Prijava Google računom
      </a>
    </div>
  )
}
