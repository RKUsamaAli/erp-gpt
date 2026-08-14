import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { CHAT_SERVICE } from './core/chat-service';
import { MockChatService } from './core/mock-chat-service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideHttpClient(withFetch()),
    // The seam. Swapping to HttpChatService once the Phase 4 agent exists
    // (agent/README.md) is this one line — nothing else in the app changes.
    { provide: CHAT_SERVICE, useClass: MockChatService },
  ],
};
