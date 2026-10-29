import { Pipe, PipeTransform } from '@angular/core';

export type IstDateFormat = 'date' | 'datetime';

const parts = (format: IstDateFormat) =>
  new Intl.DateTimeFormat('en-GB', {
    timeZone: 'Asia/Kolkata',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    ...(format === 'datetime' ? { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' } : {}),
  });

/** Fixed three-letter months – browsers differ ("Sept" in newer locale data). */
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

const FORMATTERS: Readonly<Record<IstDateFormat, Intl.DateTimeFormat>> = {
  date: parts('date'),
  datetime: parts('datetime'),
};

/** "27/Oct/2026" / "27/Oct/2026 18:00" in IST (format set by Akash – ADR-014), from an ISO string or Date. */
export function formatIst(value: string | Date, format: IstDateFormat = 'date'): string {
  const map = Object.fromEntries(
    FORMATTERS[format].formatToParts(new Date(value)).map((p) => [p.type, p.value]),
  );
  const date = `${map['day']}/${MONTHS[Number(map['month']) - 1]}/${map['year']}`;
  return format === 'datetime' ? `${date} ${map['hour']}:${map['minute']}` : date;
}

@Pipe({ name: 'istDate' })
export class IstDatePipe implements PipeTransform {
  transform(value: string | Date | null | undefined, format: IstDateFormat = 'date'): string {
    return value ? formatIst(value, format) : '';
  }
}

/** "just now", "5 min ago", "3 h ago", then the IST date and time. */
export function relativeTime(iso: string, now = new Date()): string {
  const minutes = Math.floor((now.getTime() - new Date(iso).getTime()) / 60_000);
  if (minutes < 1) {
    return 'just now';
  }
  if (minutes < 60) {
    return `${minutes} min ago`;
  }
  if (minutes < 24 * 60) {
    return `${Math.floor(minutes / 60)} h ago`;
  }
  return formatIst(iso, 'datetime');
}
