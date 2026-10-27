import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type AvatarSize = 'sm' | 'md' | 'lg';

const AVATAR_PALETTE_SIZE = 8;
const SIZE_PX: Readonly<Record<AvatarSize, number>> = { sm: 24, md: 32, lg: 40 };

/** Initials of the first and last word, e.g. "Akash Badgujar" -> "AB", "nitin" -> "N". */
export function initialsOf(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) {
    return '?';
  }
  const first = words[0][0];
  const last = words.length > 1 ? words[words.length - 1][0] : '';
  return (first + last).toUpperCase();
}

/** Stable palette slot (1..8) for a name so the same person always gets the same colour. */
export function paletteSlotOf(name: string): number {
  let hash = 0;
  for (const char of name.trim().toLowerCase()) {
    hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  }
  return (hash % AVATAR_PALETTE_SIZE) + 1;
}

/** Circle with the user's initials. `color` (Users.AvatarColor) overrides the name-derived palette colour. */
@Component({
  selector: 'app-user-avatar',
  template: `{{ initials() }}`,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
      border-radius: 50%;
      color: var(--qa-avatar-text);
      font-weight: 600;
      letter-spacing: 0.02em;
      user-select: none;
    }
  `,
  host: {
    role: 'img',
    '[attr.aria-label]': 'name()',
    '[attr.title]': 'name()',
    '[style.background]': 'background()',
    '[style.width.px]': 'sizePx()',
    '[style.height.px]': 'sizePx()',
    '[style.font-size.px]': 'fontSizePx()',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserAvatar {
  readonly name = input.required<string>();
  readonly color = input<string | null>(null);
  readonly size = input<AvatarSize>('md');

  protected readonly initials = computed(() => initialsOf(this.name()));
  protected readonly background = computed(
    () => this.color() ?? `var(--qa-avatar-${paletteSlotOf(this.name())})`,
  );
  protected readonly sizePx = computed(() => SIZE_PX[this.size()]);
  protected readonly fontSizePx = computed(() => Math.round(this.sizePx() * 0.4));
}
