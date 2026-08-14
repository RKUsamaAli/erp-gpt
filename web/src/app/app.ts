import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { LayoutService } from './core/layout.service';
import { SidebarComponent } from './shell/sidebar/sidebar.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, SidebarComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly layout = inject(LayoutService);

  readonly sidebarOpen = this.layout.sidebarOpen;

  closeSidebar(): void {
    this.layout.closeSidebar();
  }
}
