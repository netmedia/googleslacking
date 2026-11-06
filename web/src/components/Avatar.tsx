export function Avatar({ name, url, size = 36 }: { name: string; url: string | null; size?: number }) {
  const initials = name
    .split(/[\s@.]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toUpperCase())
    .join('')
  // Netmedia palette instead of random hues.
  const palette = ['#db363f', '#333333', '#7d858d', '#b92d35', '#505962', '#3e9c6a', '#c98a1c', '#8a5a44']
  let h = 0
  for (const ch of name) h = (h * 31 + ch.charCodeAt(0)) >>> 0
  return url ? (
    <img className="avatar" src={url} alt="" width={size} height={size} referrerPolicy="no-referrer" />
  ) : (
    <span className="avatar" style={{ width: size, height: size, background: palette[h % palette.length], fontSize: size * 0.4 }}>
      {initials}
    </span>
  )
}
