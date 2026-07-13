import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { AuthService } from '../../../core/services/auth.service';
import { environment } from '../../../../environments/environment';

interface ServiceCategory {
  id: number;
  name: string;
}

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatSelectModule,
    MatProgressSpinnerModule,
    MatRadioModule
  ],
  templateUrl: './register.component.html',
  styleUrls: ['./register.component.css']
})
export class RegisterComponent implements OnInit {
  registerForm: FormGroup;
  isLoading = false;
  hidePassword = true;
  hideConfirmPassword = true;
  errorMessage: string | null = null;
  isVendor = false;
  categories: ServiceCategory[] = [];

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router,
    private http: HttpClient
  ) {
    this.registerForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(6)]],
      confirmPassword: ['', [Validators.required]],
      fullName: ['', [Validators.required]],
      role: ['Customer', [Validators.required]],
      companyName: [''],
      phoneNumber: [''],
      address: [''],
      // Vendor only: both feed job matching.
      skills: [''],
      serviceCategoryIds: [[]]
    }, {
      validators: this.passwordMatchValidator
    });
  }

  ngOnInit(): void {
    this.http.get<ServiceCategory[]>(`${environment.apiUrl}/ServiceCategories`).subscribe({
      next: (categories) => (this.categories = categories),
      error: (err) => console.error('Failed to load service categories', err)
    });
  }

  passwordMatchValidator(form: FormGroup) {
    const password = form.get('password')?.value;
    const confirm = form.get('confirmPassword')?.value;
    return password === confirm ? null : { mismatch: true };
  }

  onRoleChange(role: string): void {
    this.isVendor = role === 'Vendor';

    // A vendor without a trade cannot be matched to anything, so require both.
    const required: Array<[string, boolean]> = [
      ['companyName', true],
      ['serviceCategoryIds', true]
    ];

    for (const [name] of required) {
      const control = this.registerForm.get(name);
      if (this.isVendor) {
        control?.setValidators([Validators.required]);
      } else {
        control?.clearValidators();
      }
      control?.updateValueAndValidity();
    }
  }

  onSubmit(): void {
    if (this.registerForm.invalid) {
      this.registerForm.markAllAsTouched();
      return;
    }

    this.isLoading = true;
    this.errorMessage = null;

    const formData = this.registerForm.value;
    const payload = {
      email: formData.email,
      password: formData.password,
      fullName: formData.fullName,
      role: formData.role,
      companyName: formData.role === 'Vendor' ? formData.companyName : undefined,
      phoneNumber: formData.phoneNumber,
      address: formData.address,
      // Skills and categories are what job matching ranks vendors on.
      skills: formData.role === 'Vendor' ? formData.skills : undefined,
      serviceCategoryIds: formData.role === 'Vendor' ? formData.serviceCategoryIds : undefined
    };

    this.authService.register(payload).subscribe({
      next: () => {
        this.isLoading = false;
        this.router.navigate(['/jobs']);
      },
      error: (err) => {
        this.isLoading = false;
        this.errorMessage = err.error?.message || 'Registration failed. Please try again.';
      }
    });
  }
}