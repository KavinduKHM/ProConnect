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

  constructor(
    private fb: FormBuilder,
    private jobService: JobService,
    private authService: AuthService,
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
      this.analyzeImage(file);
    }
  }

  analyzeImage(file: File): void {
    this.isAnalyzing = true;
    const formData = new FormData();
    formData.append('file', file);

    this.http.post<any>(`${environment.apiUrl}/Ai/analyze-image`, formData)
      .subscribe({
        next: (data) => {
          this.isAnalyzing = false;
          // Patch the form with AI-generated data
          if (data.title) this.jobForm.patchValue({ title: data.title });
          if (data.description) this.jobForm.patchValue({ description: data.description });
          if (data.estimatedBudgetMin) this.jobForm.patchValue({ budgetMin: data.estimatedBudgetMin });
          if (data.estimatedBudgetMax) this.jobForm.patchValue({ budgetMax: data.estimatedBudgetMax });
          if (data.isUrgent !== undefined) this.jobForm.patchValue({ isUrgent: data.isUrgent });
          if (data.suggestedCategory) {
            // Find category id by name
            const cat = this.categories.find(c => c.name.toLowerCase() === data.suggestedCategory.toLowerCase());
            if (cat) {
              this.jobForm.patchValue({ serviceCategoryId: cat.id });
            }
          }
          this.snackBar.open('AI analyzed the image and filled the form.', 'Close', { duration: 3000 });
        },
        error: (err) => {
          this.isAnalyzing = false;
          console.error('AI analysis failed', err);
          this.snackBar.open('AI analysis failed. Please fill the form manually.', 'Close', { duration: 3000 });
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