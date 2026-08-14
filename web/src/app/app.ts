import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { LayoutService } from './core/layout.service';
import { SidebarComponent } from './shell/sidebar/sidebar.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, SidebarComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  host: {
    '[class.sidebar-collapsed]': 'sidebarCollapsed()',
  },
})
export class App {
  private readonly layout = inject(LayoutService);

  readonly sidebarOpen = this.layout.sidebarOpen;
  readonly sidebarCollapsed = this.layout.sidebarCollapsed;

  closeSidebar(): void {
    this.layout.closeSidebar();
  }
}
