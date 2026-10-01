import { TestBed } from '@angular/core/testing';
import { CompoundInterestPage } from './compound-interest-page';

describe('CompoundInterestPage', () => {
  beforeEach(() => localStorage.clear());

  it('renders the projected balance in DKK by default', async () => {
    const fixture = TestBed.createComponent(CompoundInterestPage);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const value = el.querySelector('.hero__value')?.textContent ?? '';
    expect(value).toContain('kr.');
    expect(el.querySelector('app-growth-chart svg path.area--interest')).toBeTruthy();
  });
});
