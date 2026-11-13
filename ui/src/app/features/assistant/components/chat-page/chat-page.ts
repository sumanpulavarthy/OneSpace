import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  afterRenderEffect,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import { AssistantStore } from '../../data-access/assistant.store';
import { ConnectOutcome, ZohoConnectionStore, parseConnectOutcome } from '../../data-access/zoho-connection.store';
import { Composer } from '../composer/composer';
import { MessageBubble } from '../message-bubble/message-bubble';

@Component({
  selector: 'app-chat-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Composer, MessageBubble],
  providers: [AssistantStore, ZohoConnectionStore],
  templateUrl: './chat-page.html',
  styleUrl: './chat-page.scss',
})
export class ChatPage implements OnInit {
  protected readonly store = inject(AssistantStore);
  protected readonly zoho = inject(ZohoConnectionStore);
  private readonly router = inject(Router);
  private readonly log = viewChild<ElementRef<HTMLElement>>('log');
  private readonly composer = viewChild.required(Composer);

  /** ?zoho=connected|failed, set by the API after Zoho's consent page (route input binding). */
  readonly zohoResult = input<string | undefined>(undefined, { alias: 'zoho' });
  protected readonly banner = signal<ConnectOutcome>(null);
  protected readonly connectError = signal<string | null>(null);

  /**
   * Names only systems with a real, confirmed connection. Today that is just Zoho People (per-user consent).
   * ComplyKaro runs on sample data and Microsoft 365 has no status query yet, so neither is claimed.
   */
  protected readonly connectedLine = computed(() => (this.zoho.connected() ? 'Connected to Zoho People.' : null));

  protected readonly suggestions = [
    "What's my leave balance?",
    "Where's the WFH policy?",
    'Which holidays are coming up?',
    'Am I free this week?',
  ];

  constructor() {
    // Keep the newest message in view.
    afterRenderEffect(() => {
      this.store.messages();
      this.store.pending();
      const el = this.log()?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }

  ngOnInit(): void {
    void this.store.loadUser();
    void this.zoho.load();

    const outcome = parseConnectOutcome(this.zohoResult());
    if (outcome) {
      this.banner.set(outcome);
      // Drop the query string so a reload doesn't repeat the banner.
      void this.router.navigate([], { queryParams: { zoho: null }, queryParamsHandling: 'merge', replaceUrl: true });
    }
  }

  protected ask(question: string): void {
    void this.store.ask(question);
  }

  protected useSuggestion(question: string): void {
    this.composer().submit(question);
  }

  protected async connectZoho(): Promise<void> {
    this.connectError.set(null);
    try {
      await this.zoho.connect();
    } catch (error) {
      this.connectError.set((error as Error).message);
    }
  }
}
