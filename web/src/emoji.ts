/** Curated emoji set for the picker (unicode + search keywords, English and Croatian). */
export interface EmojiDef {
  e: string
  k: string
}

export const EMOJI_GROUPS: { name: string; items: EmojiDef[] }[] = [
  {
    name: 'Često',
    items: [
      { e: '👍', k: 'thumbsup +1 like palac da ok' },
      { e: '❤️', k: 'heart love srce' },
      { e: '😂', k: 'joy laugh smijeh' },
      { e: '🎉', k: 'tada party slavlje' },
      { e: '🙏', k: 'pray thanks hvala molim' },
      { e: '👀', k: 'eyes look gledam' },
      { e: '✅', k: 'check done gotovo' },
      { e: '🔥', k: 'fire vatra' },
      { e: '🚀', k: 'rocket launch raketa' },
      { e: '💯', k: '100 hundred' },
      { e: '👏', k: 'clap pljesak bravo' },
      { e: '🙌', k: 'raised hands hura' },
    ],
  },
  {
    name: 'Lica',
    items: [
      { e: '😀', k: 'grin smile osmijeh' }, { e: '😃', k: 'smiley' }, { e: '😄', k: 'smile' }, { e: '😁', k: 'beam' },
      { e: '😅', k: 'sweat smile' }, { e: '🤣', k: 'rofl' }, { e: '🙂', k: 'slight smile' }, { e: '😉', k: 'wink namig' },
      { e: '😊', k: 'blush' }, { e: '😇', k: 'innocent' }, { e: '🥰', k: 'love' }, { e: '😍', k: 'heart eyes' },
      { e: '😘', k: 'kiss poljubac' }, { e: '😋', k: 'yum' }, { e: '😛', k: 'tongue' }, { e: '😜', k: 'crazy' },
      { e: '🤪', k: 'zany' }, { e: '🤔', k: 'thinking mislim hmm' }, { e: '🤨', k: 'raised eyebrow' }, { e: '😐', k: 'neutral' },
      { e: '😑', k: 'expressionless' }, { e: '😶', k: 'no mouth' }, { e: '🙄', k: 'eye roll' }, { e: '😏', k: 'smirk' },
      { e: '😬', k: 'grimace' }, { e: '😌', k: 'relieved' }, { e: '😴', k: 'sleep spavam' }, { e: '🤒', k: 'sick bolestan' },
      { e: '🤯', k: 'mind blown' }, { e: '🥳', k: 'party' }, { e: '😎', k: 'cool sunglasses' }, { e: '🤓', k: 'nerd' },
      { e: '😕', k: 'confused' }, { e: '😟', k: 'worried' }, { e: '😮', k: 'open mouth wow' }, { e: '😲', k: 'astonished' },
      { e: '😳', k: 'flushed' }, { e: '🥺', k: 'pleading' }, { e: '😢', k: 'cry plač' }, { e: '😭', k: 'sob' },
      { e: '😱', k: 'scream' }, { e: '😤', k: 'triumph' }, { e: '😡', k: 'angry ljut' }, { e: '🤬', k: 'cursing' },
      { e: '💀', k: 'skull' }, { e: '🤡', k: 'clown' }, { e: '👻', k: 'ghost' }, { e: '🤖', k: 'robot' },
      { e: '🫠', k: 'melting' }, { e: '🫡', k: 'salute' }, { e: '🤝', k: 'handshake dogovor' }, { e: '🤷', k: 'shrug' },
      { e: '🤦', k: 'facepalm' },
    ],
  },
  {
    name: 'Ruke',
    items: [
      { e: '👋', k: 'wave bok' }, { e: '✋', k: 'hand' }, { e: '👌', k: 'ok' }, { e: '✌️', k: 'victory' },
      { e: '🤞', k: 'crossed fingers' }, { e: '🤘', k: 'rock' }, { e: '👈', k: 'left' }, { e: '👉', k: 'right' },
      { e: '👆', k: 'up' }, { e: '👇', k: 'down' }, { e: '👎', k: 'thumbsdown -1 ne' }, { e: '👊', k: 'fist' },
      { e: '💪', k: 'muscle snaga' }, { e: '✍️', k: 'writing' }, { e: '🫶', k: 'heart hands' },
    ],
  },
  {
    name: 'Simboli',
    items: [
      { e: '💚', k: 'green heart' }, { e: '💙', k: 'blue heart' }, { e: '💜', k: 'purple heart' }, { e: '🖤', k: 'black heart' },
      { e: '💔', k: 'broken heart' }, { e: '⭐', k: 'star zvijezda' }, { e: '✨', k: 'sparkles' }, { e: '⚡', k: 'zap' },
      { e: '💡', k: 'idea ideja' }, { e: '❗', k: 'exclamation' }, { e: '❓', k: 'question pitanje' }, { e: '⚠️', k: 'warning upozorenje' },
      { e: '❌', k: 'x no' }, { e: '⛔', k: 'no entry' }, { e: '🚫', k: 'forbidden' }, { e: '✔️', k: 'check' },
      { e: '➕', k: 'plus' }, { e: '🆗', k: 'ok' }, { e: '🆕', k: 'new novo' }, { e: '🔴', k: 'red' },
      { e: '🟢', k: 'green' }, { e: '🟡', k: 'yellow' }, { e: '🔵', k: 'blue' }, { e: '⏰', k: 'alarm' },
      { e: '⏳', k: 'hourglass čekam' }, { e: '📌', k: 'pin' }, { e: '📎', k: 'paperclip' }, { e: '🔗', k: 'link' },
    ],
  },
  {
    name: 'Posao',
    items: [
      { e: '💻', k: 'laptop' }, { e: '🖥️', k: 'desktop' }, { e: '📱', k: 'phone mobitel' }, { e: '⌨️', k: 'keyboard' },
      { e: '🐛', k: 'bug' }, { e: '🛠️', k: 'tools alat' }, { e: '⚙️', k: 'gear settings' }, { e: '📈', k: 'chart up' },
      { e: '📉', k: 'chart down' }, { e: '📊', k: 'bar chart' }, { e: '📅', k: 'calendar kalendar' }, { e: '📝', k: 'memo note' },
      { e: '📄', k: 'document' }, { e: '📁', k: 'folder' }, { e: '📦', k: 'package deploy' }, { e: '🔒', k: 'lock' },
      { e: '🔑', k: 'key' }, { e: '📣', k: 'megaphone' }, { e: '📢', k: 'loudspeaker' }, { e: '🏁', k: 'finish' },
      { e: '🎯', k: 'target' }, { e: '🧪', k: 'test' }, { e: '☕', k: 'coffee kava' }, { e: '🍕', k: 'pizza' },
      { e: '🍺', k: 'beer pivo' }, { e: '🎂', k: 'cake rođendan' }, { e: '🏖️', k: 'beach godišnji' }, { e: '🤿', k: 'diving ronjenje' },
    ],
  },
]

export const ALL_EMOJI = EMOJI_GROUPS.flatMap((g) => g.items)
