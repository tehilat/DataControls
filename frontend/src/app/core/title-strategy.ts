import { inject, Injectable } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

export const APP_NAME = 'רדאר';
export const APP_TITLE = 'רדאר - בקרות איכות נתונים';

/** "<page> · רדאר" in the browser tab; the full system name on the home page. */
@Injectable({ providedIn: 'root' })
export class HebrewTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);
    this.title.setTitle(page ? `${page} · ${APP_NAME}` : APP_TITLE);
  }
}
