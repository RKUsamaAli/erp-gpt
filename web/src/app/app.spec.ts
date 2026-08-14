import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { CHAT_SERVICE } from './core/chat-service';
import { MockChatService } from './core/mock-chat-service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      // App renders ChatComponent, which injects CHAT_SERVICE. The token has no
      // providedIn by design, so every TestBed that mounts the tree must supply it.
      providers: [{ provide: CHAT_SERVICE, useClass: MockChatService }],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render the chat component', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-chat')).toBeTruthy();
  });
});
