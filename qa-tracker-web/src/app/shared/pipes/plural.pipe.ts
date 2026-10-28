import { Pipe, PipeTransform } from '@angular/core';

/** "1 module", "3 modules": `{{ count | plural: 'module' }}` (irregular: `plural: 'person' : 'people'`). */
@Pipe({ name: 'plural' })
export class PluralPipe implements PipeTransform {
  transform(count: number, singular: string, pluralForm = `${singular}s`): string {
    return `${count} ${count === 1 ? singular : pluralForm}`;
  }
}
