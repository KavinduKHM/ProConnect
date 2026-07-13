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
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatDividerModule } from '@angular/material/divider';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSelectModule } from '@angular/material/select';
import { ProfileService, FullProfileResponse } from '../../core/services/profile.service';
import { AuthService } from '../../core/services/auth.service';
import { environment } from '../../../environments/environment';

interface ServiceCategory {
  id: number;
  name: string;
}

@Component({
  selector: 'app-profile',
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
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatDividerModule,
    MatSnackBarModule,
    MatSelectModule
  ],
  templateUrl: './profile.component.html',
  styleUrls: ['./profile.component.css']
})
export class ProfileComponent implements OnInit {
  profileForm: FormGroup;
  isLoading = false;
  isSaving = false;
  isVendor = false;
  profileData: FullProfileResponse | null = null;
  categories: ServiceCategory[] = [];

  constructor(
    private fb: FormBuilder,
    private profileService: ProfileService,
    private authService: AuthService,
    private snackBar: MatSnackBar,
    private router: Router,
    private http: HttpClient
  ) {
    this.profileForm = this.fb.group({
      fullName: ['', [Validators.required]],
      phoneNumber: [''],
      address: [''],
      companyName: [''],
      description: [''],
      website: [''],
      isAvailable: [false],
      skills: [''],
      serviceCategoryIds: [[]]
    });
  }

  ngOnInit(): void {
    this.loadProfile();
    this.http.get<ServiceCategory[]>(`${environment.apiUrl}/ServiceCategories`).subscribe({
      next: (categories) => (this.categories = categories),
      error: (err) => console.error('Failed to load service categories', err)
    });
  }

  loadProfile(): void {
    this.isLoading = true;
    this.profileService.getProfile().subscribe({
      next: (data) => {
        this.profileData = data;
        this.isVendor = data.user.isVendor;
        // Populate form
        this.profileForm.patchValue({
          fullName: data.user.fullName,
          phoneNumber: data.user.phoneNumber || '',
          address: data.vendor?.address || data.customer?.address || ''
        });
        if (this.isVendor && data.vendor) {
          this.profileForm.patchValue({
            companyName: data.vendor.companyName || '',
            description: data.vendor.description || '',
            website: data.vendor.website || '',
            isAvailable: data.vendor.isAvailable,
            skills: data.vendor.skills || '',
            serviceCategoryIds: data.vendor.serviceCategoryIds || []
          });
        }
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Failed to load profile', err);
        this.isLoading = false;
        this.snackBar.open('Failed to load profile.', 'Close', { duration: 3000 });
      }
    });
  }

  onSubmit(): void {
    if (this.profileForm.invalid) {
      this.profileForm.markAllAsTouched();
      return;
    }

    this.isSaving = true;
    const formValue = this.profileForm.value;
    const payload: any = {
      fullName: formValue.fullName,
      phoneNumber: formValue.phoneNumber,
      address: formValue.address
    };
    if (this.isVendor) {
      payload.companyName = formValue.companyName;
      payload.description = formValue.description;
      payload.website = formValue.website;
      payload.isAvailable = formValue.isAvailable;
      payload.skills = formValue.skills;
      payload.serviceCategoryIds = formValue.serviceCategoryIds;
    }

    this.profileService.updateProfile(payload).subscribe({
      next: () => {
        this.isSaving = false;
        this.snackBar.open('Profile updated successfully!', 'Close', { duration: 3000 });
        // Update auth service user info
        this.authService.updateCurrentUser({
          fullName: payload.fullName,
          phoneNumber: payload.phoneNumber
        });
        // Reload profile to reflect changes
        this.loadProfile();
      },
      error: (err) => {
        this.isSaving = false;
        console.error('Failed to update profile', err);
        this.snackBar.open('Failed to update profile.', 'Close', { duration: 3000 });
      }
    });
  }
}