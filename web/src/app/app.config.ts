import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { CHAT_SERVICE } from './core/chat-service';
import { CHAT_STORE } from './core/chat-store';
import { LocalChatStore } from './core/local-chat-store';
import { HttpChatService } from './core/http-chat-service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideHttpClient(withFetch()),
    // withComponentInputBinding lets route params arrive as component inputs,
    // so no component has to inject ActivatedRoute to read :chatId.
    provideRouter(routes, withComponentInputBinding()),
    // The two seams. Chat answers use the deployed GraphQL API; workspace
    // storage remains local until ChatThread/ChatTurn entities land.
    { provide: CHAT_SERVICE, useClass: HttpChatService },
    { provide: CHAT_STORE, useClass: LocalChatStore },
  ],
};
