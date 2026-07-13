import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http'; // Import HttpClient

import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatIconModule } from '@angular/material/icon';
import { MatDividerModule } from '@angular/material/divider';

import { JobService, CreateJobRequest } from '../../../core/services/job.service';
import { AuthService } from '../../../core/services/auth.service';
import { AiService, BudgetEstimate } from '../../../core/services/ai.service';
import { environment } from '../../../../environments/environment';

interface ServiceCategory {
  id: number;
  name: string;
}

@Component({
  selector: 'app-job-create',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatSelectModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatIconModule,
    MatDividerModule
  ],
  templateUrl: './job-create.component.html',
  styleUrls: ['./job-create.component.css']
})
export class JobCreateComponent implements OnInit {
  jobForm: FormGroup;
  isLoading = false;
  isAnalyzing = false;
  errorMessage: string | null = null;
  categories: ServiceCategory[] = [];
  uploadedFileName: string | null = null;

  isImproving = false;
  isEstimating = false;
  budgetEstimate: BudgetEstimate | null = null;

  /** Where the API stored the uploaded photo; travels with the job so vendors can see it. */
  imageUrl: string | null = null;
  imagePreview: string | null = null;

  constructor(
    private fb: FormBuilder,
    private jobService: JobService,
    private authService: AuthService,
    private aiService: AiService,
    private router: Router,
    private http: HttpClient,
    private snackBar: MatSnackBar
  ) {
    this.jobForm = this.fb.group({
      title: ['', [Validators.required, Validators.minLength(5)]],
      description: ['', [Validators.required, Validators.minLength(20)]],
      serviceCategoryId: ['', [Validators.required]],
      location: [''],
      budgetMin: ['', [Validators.required, Validators.min(1)]],
      budgetMax: ['', [Validators.required, Validators.min(1)]],
      preferredDate: ['', [Validators.required]],
      isUrgent: [false]
    }, {
      validators: this.budgetRangeValidator
    });
  }

  budgetRangeValidator(form: FormGroup) {
    const min = form.get('budgetMin')?.value;
    const max = form.get('budgetMax')?.value;
    if (min && max && max < min) {
      return { budgetInvalid: true };
    }
    return null;
  }

  ngOnInit(): void {
    this.loadCategories();
  }

  loadCategories(): void {
    this.http.get<ServiceCategory[]>(`${environment.apiUrl}/ServiceCategories`)
      .subscribe({
        next: (data) => {
          this.categories = data;
        },
        error: (err) => {
          console.error('Failed to load categories', err);
          // Fallback categories (should match seeded data)
          this.categories = [
            { id: 1, name: 'Plumbing' },
            { id: 2, name: 'Electrical' },
            { id: 3, name: 'Painting' },
            { id: 4, name: 'Transport' },
            { id: 5, name: 'Carpentry' },
            { id: 6, name: 'Cleaning' },
            { id: 7, name: 'HVAC' },
            { id: 8, name: 'Gardening' }
          ];
        }
      });
  }

  // ------------------ AI Image Upload ------------------
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];
      // Validate file type
      const allowedTypes = ['image/jpeg', 'image/png', 'image/gif', 'image/webp', 'image/bmp'];
      if (!allowedTypes.includes(file.type)) {
        this.snackBar.open('Only image files are allowed.', 'Close', { duration: 3000 });
        return;
      }
      // Validate file size (max 5 MB)
      if (file.size > 5_000_000) {
        this.snackBar.open('File size exceeds 5 MB.', 'Close', { duration: 3000 });
        return;
      }

      this.uploadedFileName = file.name;

      // Show the photo straight away rather than waiting on the AI round-trip.
      const reader = new FileReader();
      reader.onload = () => (this.imagePreview = reader.result as string);
      reader.readAsDataURL(file);

      this.analyzeImage(file);
    }
  }

  analyzeImage(file: File): void {
    this.isAnalyzing = true;

    this.aiService.analyzeImage(file).subscribe({
      next: (data) => {
        this.isAnalyzing = false;

        // The API kept the photo — carry its URL through to the job so vendors see it.
        this.imageUrl = data.imageUrl ?? null;

        // Don't auto-fill the form from a photo with no home-service issue in it.
        if (data.isRelevant === false) {
          this.snackBar.open(
            "That photo doesn't look like a home-service problem. Upload a photo of the issue, or fill the form in yourself.",
            'Close',
            { duration: 7000 }
          );
          return;
        }

        if (data.title) this.jobForm.patchValue({ title: data.title });
        if (data.description) this.jobForm.patchValue({ description: data.description });
        if (data.estimatedBudgetMin) this.jobForm.patchValue({ budgetMin: data.estimatedBudgetMin });
        if (data.estimatedBudgetMax) this.jobForm.patchValue({ budgetMax: data.estimatedBudgetMax });
        if (data.isUrgent !== undefined) this.jobForm.patchValue({ isUrgent: data.isUrgent });
        if (data.suggestedCategory) {
          const cat = this.categories.find(
            c => c.name.toLowerCase() === data.suggestedCategory!.toLowerCase()
          );
          if (cat) {
            this.jobForm.patchValue({ serviceCategoryId: cat.id });
          }
        }
        this.snackBar.open('AI analyzed the image and filled the form.', 'Close', { duration: 3000 });
      },
      error: (err) => {
        this.isAnalyzing = false;
        console.error('AI analysis failed', err);
        const reason = err.error?.message;
        this.snackBar.open(
          reason ? `AI analysis failed: ${reason}` : 'AI analysis failed. Please fill the form manually.',
          'Close',
          { duration: 6000 }
        );
      }
    });
  }

  /**
   * Suggests a budget range anchored in what jobs like this actually completed for on the platform,
   * rather than what the model imagines things cost.
   */
  estimateBudget(): void {
    const description = this.jobForm.value.description;
    const categoryId = +this.jobForm.value.serviceCategoryId;

    if (!description || description.trim().length < 10) {
      this.snackBar.open('Describe the job first so the estimate has something to go on.', 'Close', { duration: 4000 });
      return;
    }
    if (!categoryId) {
      this.snackBar.open('Pick a service category first.', 'Close', { duration: 4000 });
      return;
    }

    this.isEstimating = true;
    this.budgetEstimate = null;
    this.aiService.estimateBudget(description, categoryId, this.jobForm.value.title).subscribe({
      next: (estimate) => {
        this.isEstimating = false;
        this.budgetEstimate = estimate;
        this.jobForm.patchValue({
          budgetMin: estimate.estimatedMin,
          budgetMax: estimate.estimatedMax
        });
      },
      error: (err) => {
        this.isEstimating = false;
        this.snackBar.open(err.error?.message || 'Could not estimate a budget.', 'Close', { duration: 5000 });
      }
    });
  }

  /** Rewrites whatever the customer typed into something a vendor can actually bid on. */
  improveDescription(): void {
    const description = this.jobForm.value.description;
    if (!description || description.trim().length < 10) {
      this.snackBar.open('Write a rough description first, then let the AI polish it.', 'Close', { duration: 4000 });
      return;
    }

    const category = this.categories.find(c => c.id === +this.jobForm.value.serviceCategoryId)?.name;

    this.isImproving = true;
    this.aiService.improveDescription(description, this.jobForm.value.title, category).subscribe({
      next: (result) => {
        this.isImproving = false;
        this.jobForm.patchValue({ description: result.improvedDescription });
        this.snackBar.open('Description rewritten by AI.', 'Close', { duration: 3000 });
      },
      error: (err) => {
        this.isImproving = false;
        this.snackBar.open(err.error?.message || 'Could not improve the description.', 'Close', { duration: 5000 });
      }
    });
  }
  // ----------------------------------------------------

  onSubmit(): void {
    if (this.jobForm.invalid) {
      this.jobForm.markAllAsTouched();
      return;
    }

    this.isLoading = true;
    this.errorMessage = null;

    const formData = this.jobForm.value;
    const payload: CreateJobRequest = {
      title: formData.title,
      description: formData.description,
      imageUrl: this.imageUrl ?? undefined,
      serviceCategoryId: +formData.serviceCategoryId,
      location: formData.location,
      budgetMin: +formData.budgetMin,
      budgetMax: +formData.budgetMax,
      preferredDate: new Date(formData.preferredDate).toISOString(),
      isUrgent: formData.isUrgent
    };

    this.jobService.createJob(payload).subscribe({
      next: (job) => {
        this.isLoading = false;
        this.router.navigate(['/jobs', job.id]);
      },
      error: (err) => {
        this.isLoading = false;
        this.errorMessage = err.error?.message || 'Failed to create job. Please try again.';
      }
    });
  }
}