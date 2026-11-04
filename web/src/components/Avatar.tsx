export function Avatar({ name, url, size = 36 }: { name: string; url: string | null; size?: number }) {
  const initials = name
    .split(/[\s@.]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toUpperCase())
    .join('')
  let hue = 0
  for (const ch of name) hue = (hue * 31 + ch.charCodeAt(0)) % 360
  return url ? (
    <img className="avatar" src={url} alt="" width={size} height={size} referrerPolicy="no-referrer" />
  ) : (
    <span className="avatar" style={{ width: size, height: size, background: `hsl(${hue} 45% 45%)`, fontSize: size * 0.4 }}>
      {initials}
    </span>
  )
}
