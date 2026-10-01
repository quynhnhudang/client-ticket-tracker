import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CATEGORY_LABELS, PRIORITY_LABELS } from '../../../core/labels';
import { CATEGORIES, PRIORITIES, TicketCategory, TicketPriority } from '../../../core/models';
import { fieldErrors, problemMessage } from '../../../core/problem';
import { TicketsApi } from '../../../core/tickets.api';

type FieldName = 'title' | 'description' | 'category' | 'priority' | 'clientName' | 'clientEmail';

/**
 * Intake form.
 *
 * The validators mirror the attributes on CreateTicketRequest (Dtos/Requests.cs)
 * so agents see problems before submitting. They are a convenience only: the
 * API validates again, and any server-side errors are shown next to the fields.
 *
 * Optional LLM triage: if the server has it configured, "Suggest" asks it for a
 * category and priority. The suggestion only PRE-FILLS the dropdowns, marked
 * "suggested"; nothing is saved until the agent reviews it and clicks Create.
 */
@Component({
  selector: 'app-ticket-new',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './ticket-new.html',
})
export class TicketNew {
  private readonly api = inject(TicketsApi);
  private readonly router = inject(Router);

  protected readonly categories = CATEGORIES;
  protected readonly priorities = PRIORITIES;
  protected readonly categoryLabels = CATEGORY_LABELS;
  protected readonly priorityLabels = PRIORITY_LABELS;

  protected readonly form = inject(FormBuilder).nonNullable.group({
    title: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(200)]],
    description: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(4000)]],
    // '' means "not chosen yet": the agent must pick one (or accept a suggestion).
    category: ['' as TicketCategory | '', Validators.required],
    priority: ['' as TicketPriority | '', Validators.required],
    clientName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(200)]],
    clientEmail: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
  });

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});

  // Triage suggestion state.
  protected readonly triageEnabled = signal(false);
  protected readonly suggesting = signal(false);
  protected readonly suggestion = signal<{ reason: string } | null>(null);
  protected readonly suggestionError = signal<string | null>(null);
  /** Which fields currently hold an unmodified AI suggestion (drives the "suggested" tag). */
  protected readonly suggestedFields = signal<{ category: boolean; priority: boolean }>({
    category: false,
    priority: false,
  });

  constructor() {
    this.api.triageEnabled().subscribe({
      next: (r) => this.triageEnabled.set(r.enabled),
      error: () => this.triageEnabled.set(false),
    });

    // As soon as the agent changes a suggested field, it's their choice, not the model's.
    this.form.controls.category.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.clearSuggestedTag('category'));
    this.form.controls.priority.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.clearSuggestedTag('priority'));
  }

  protected showError(name: FieldName): string | null {
    const server = this.serverErrors()[name];
    if (server) return server;
    const control = this.form.controls[name];
    if (!control.touched || control.valid) return null;
    if (control.hasError('required')) return 'Required.';
    if (control.hasError('email')) return 'Enter a valid email address.';
    if (control.hasError('minlength')) return `At least ${control.getError('minlength').requiredLength} characters.`;
    if (control.hasError('maxlength')) return `At most ${control.getError('maxlength').requiredLength} characters.`;
    return 'Invalid value.';
  }

  protected canSuggest(): boolean {
    const { title, description } = this.form.controls;
    return this.triageEnabled() && !this.suggesting() && title.valid && description.valid;
  }

  protected suggest(): void {
    if (!this.canSuggest()) return;
    this.suggesting.set(true);
    this.suggestionError.set(null);

    const { title, description } = this.form.getRawValue();
    this.api.suggestTriage(title, description).subscribe({
      next: (s) => {
        // emitEvent: false so these programmatic changes don't trigger the
        // valueChanges handlers above, which would immediately clear the tags.
        this.form.controls.category.setValue(s.category, { emitEvent: false });
        this.form.controls.priority.setValue(s.priority, { emitEvent: false });
        this.suggestedFields.set({ category: true, priority: true });
        this.suggestion.set({ reason: s.reason });
        this.suggesting.set(false);
      },
      error: (err: unknown) => {
        this.suggestionError.set(`No suggestion available: ${problemMessage(err)}`);
        this.suggesting.set(false);
      },
    });
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.saving.set(true);
    this.error.set(null);
    this.serverErrors.set({});

    const value = this.form.getRawValue();
    this.api
      .create({
        ...value,
        category: value.category as TicketCategory,
        priority: value.priority as TicketPriority,
      })
      .subscribe({
        next: (ticket) => void this.router.navigate(['/tickets', ticket.id]),
        error: (err: unknown) => {
          this.serverErrors.set(fieldErrors(err));
          this.error.set(problemMessage(err));
          this.saving.set(false);
        },
      });
  }

  private clearSuggestedTag(field: 'category' | 'priority'): void {
    this.suggestedFields.update((s) => ({ ...s, [field]: false }));
  }
}
