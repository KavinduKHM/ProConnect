import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { JobService, CreateJobRequest } from '../../../core/services/job.service';
import { AuthService } from '../../../core/services/auth.service';
import { HttpClient } from '@angular/common/http';
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
    MatProgressSpinnerModule
  ],
  templateUrl: './job-create.component.html',
  styleUrls: ['./job-create.component.css']
})
export class JobCreateComponent implements OnInit {
  jobForm: FormGroup;
  isLoading = false;
  errorMessage: string | null = null;
  categories: ServiceCategory[] = [];

  constructor(
    private fb: FormBuilder,
    private jobService: JobService,
    private authService: AuthService,
    private router: Router,
    private http: HttpClient
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
        console.error('Failed to load categories from backend, using fallback', err);
        // Hardcoded fallback (these match your seeded database)
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
    // imageUrl is optional – we can omit it entirely
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