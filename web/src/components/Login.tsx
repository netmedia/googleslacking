import { loginUrl } from '../api'
import { NetmediaLogo } from '../brand'

export function Login({ error }: { error: string | null }) {
  return (
    <div className="login">
      <header className="login-top">
        <NetmediaLogo height={30} />
      </header>
      <main className="login-hero">
        <div>
          <span className="eyebrow">Netmedia · Google Chat klijent</span>
          <h1>
            Razgovori koji ne <em>usporavaju posao</em>
          </h1>
          <p>
            Brži Google Chat: obavijesti koje imaju smisla, niti, pretraga, podsjetnici i zakazane poruke — na računalu i mobitelu.
          </p>
          {error && <p className="error">{error}</p>}
          <a className="login-cta" href={loginUrl('/')}>
            Prijava Google računom <span aria-hidden>→</span>
          </a>
        </div>
        <HeroArt />
      </main>
    </div>
  )
}

/** Line illustration in the netmedia.agency style: dashed outline, chat window, bubbles, check. */
function HeroArt() {
  return (
    <svg className="login-art" viewBox="0 0 460 380" fill="none" aria-hidden>
      <path
        d="M40 190c0-90 80-160 200-160s190 60 190 150-70 170-200 170S40 280 40 190z"
        stroke="#7d858d"
        strokeWidth="2"
        strokeDasharray="6 7"
      />
      {/* chat window */}
      <rect x="140" y="80" width="170" height="210" rx="18" fill="#22272c" stroke="#a3a8ad" strokeWidth="3" />
      <circle cx="166" cy="106" r="6" fill="#ff5a63" />
      <circle cx="186" cy="106" r="6" fill="#7d858d" />
      <circle cx="206" cy="106" r="6" fill="#7d858d" />
      <rect x="160" y="135" width="95" height="22" rx="11" fill="#2f353b" />
      <rect x="195" y="168" width="95" height="22" rx="11" fill="#ff5a63" fillOpacity="0.25" stroke="#ff5a63" strokeWidth="2" />
      <rect x="160" y="201" width="70" height="22" rx="11" fill="#2f353b" />
      <line x1="160" y1="248" x2="290" y2="248" stroke="#505962" strokeWidth="3" strokeLinecap="round" />
      {/* bubble */}
      <path d="M318 70h80a18 18 0 0 1 18 18v38a18 18 0 0 1-18 18h-46l-22 18v-18h-12a18 18 0 0 1-18-18V88a18 18 0 0 1 18-18z" fill="#ff5a63" fillOpacity="0.15" stroke="#ff5a63" strokeWidth="3" />
      <circle cx="342" cy="107" r="5" fill="#ff5a63" />
      <circle cx="360" cy="107" r="5" fill="#ff5a63" />
      <circle cx="378" cy="107" r="5" fill="#ff5a63" />
      {/* bell */}
      <rect x="62" y="210" width="62" height="62" rx="14" fill="#22272c" stroke="#a3a8ad" strokeWidth="3" />
      <path d="M93 228c-9 0-14 7-14 15v9l-5 6h38l-5-6v-9c0-8-5-15-14-15zM88 262a5 5 0 0 0 10 0" stroke="#ece9e3" strokeWidth="3" strokeLinejoin="round" />
      <path d="M124 241c10 0 14-20 16-30" stroke="#ff5a63" strokeWidth="2.5" strokeDasharray="5 5" />
      {/* check */}
      <rect x="330" y="235" width="74" height="56" rx="12" fill="#22272c" stroke="#a3a8ad" strokeWidth="3" />
      <path d="M350 263l12 12 22-24" stroke="#ff5a63" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M310 230c12 0 18-6 22-10" stroke="#ff5a63" strokeWidth="2.5" strokeDasharray="5 5" />
      <circle cx="310" cy="230" r="5" fill="#ff5a63" />
    </svg>
  )
}
