import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';
import { CHAT_SERVICE } from './core/chat-service';
import { CHAT_STORE } from './core/chat-store';
import { LocalChatStore } from './core/local-chat-store';
import { MockChatService } from './core/mock-chat-service';

describe('App', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      // Neither token has providedIn by design, so every TestBed that mounts the
      // tree must supply both.
      providers: [
        provideRouter(routes),
        { provide: CHAT_SERVICE, useClass: MockChatService },
        { provide: CHAT_STORE, useClass: LocalChatStore },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the sidebar', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-sidebar')).toBeTruthy();
  });
});
