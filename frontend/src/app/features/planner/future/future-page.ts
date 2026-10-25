import { httpResource } from '@angular/common/http';
import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Dashboard, Home } from '../../../core/api/planner-api';
import { I18n } from '../../../core/i18n/i18n';
import { readStorage, writeStorage } from '../../../core/storage';
import { INCLUDE_HOME_KEY } from '../dashboard/dashboard-page';
import { CurrentCourse } from './current-course';

/** Where today's net worth goes in the next years if you keep going as now. */
@Component({
  selector: 'app-future-page',
  imports: [RouterLink, CurrentCourse],
  templateUrl: './future-page.html',
  styleUrl: './future-page.scss',
})
export class FuturePage {
  protected readonly i18n = inject(I18n);

  protected readonly includeHome = signal(readStorage(INCLUDE_HOME_KEY) !== 'false');

  protected readonly dashboard = httpResource<Dashboard>(() =>
    this.includeHome() ? '/api/dashboard' : '/api/dashboard?includeHome=false',
  );
  /** Keeps the previous dashboard on screen while the switch reloads it. */
  protected readonly view = linkedSignal<Dashboard | undefined, Dashboard | undefined>({
    source: () => this.dashboard.value(),
    computation: (value, previous) => value ?? previous?.value,
  });
  /** The "keep going as now" projection needs something to start from. */
  protected readonly course = computed(() => (this.view()?.latest ? this.view()! : null));

  private readonly homes = httpResource<Home[]>(() => '/api/homes');
  protected readonly hasHome = computed(() => (this.homes.value() ?? []).length > 0);

  protected setIncludeHome(on: boolean) {
    this.includeHome.set(on);
    writeStorage(INCLUDE_HOME_KEY, String(on));
  }
}
