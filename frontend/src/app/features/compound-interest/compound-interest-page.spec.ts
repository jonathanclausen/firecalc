import { TestBed } from '@angular/core/testing';
import { I18n } from '../../core/i18n/i18n';
import { CompoundInterestPage } from './compound-interest-page';

describe('CompoundInterestPage', () => {
  beforeEach(() => {
    localStorage.clear();
    document.cookie = 'firecalc.lang=; max-age=0; path=/';
  });

  it('renders in Danish with DKK by default', async () => {
    const fixture = TestBed.createComponent(CompoundInterestPage);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h1')?.textContent).toContain('Renters rente');
    expect(el.querySelector('.hero__value')?.textContent).toContain('kr.');
    expect(el.querySelector('app-growth-chart svg path.area--interest')).toBeTruthy();
  });

  it('switches to English and remembers the choice in a cookie', async () => {
    const fixture = TestBed.createComponent(CompoundInterestPage);
    TestBed.inject(I18n).set('en');
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h1')?.textContent).toContain('Compound interest');
    expect(document.cookie).toContain('firecalc.lang=en');
    expect(document.documentElement.lang).toBe('en');
  });
});
