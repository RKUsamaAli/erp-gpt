import { Routes } from '@angular/router';
import { ChatComponent } from './chat/chat.component';
import { openLatestChat } from './core/open-latest-chat';

/**
 * A chat is the only addressable thing. Its project is a property of the chat,
 * so the URL does not repeat it — `/project/x/chat/y` would allow states where
 * y is not in x. Projects are containers in the sidebar, not destinations.
 */
export const routes: Routes = [
  { path: 'chat/:chatId', component: ChatComponent },
  // Landing: resume the most recent chat, or start one.
  { path: '', canActivate: [openLatestChat], children: [] },
  { path: '**', redirectTo: '' },
];
