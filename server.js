// Simple Express server for handling treasure image uploads and config updates
// This server provides the endpoints that your AR treasure hunt app expects

const express = require('express');
const multer = require('multer');
const fs = require('fs').promises;
const path = require('path');
const cors = require('cors');

const app = express();
const PORT = process.env.PORT || 3001;

// Middleware
app.use(cors());
app.use(express.json());
app.use('/images', express.static(path.join(__dirname, 'images')));
// CRITICAL: Serve Unity Addressables ServerData directory
app.use('/ServerData', express.static(path.join(__dirname, 'ServerData')));

// Configure multer for file uploads with detailed logging
const storage = multer.diskStorage({
  destination: function (req, file, cb) {
    const imagesPath = path.join(__dirname, 'images');
    console.log('🗂️  Multer destination:', imagesPath);
    
    // Check if images directory exists
    const fs = require('fs');
    if (!fs.existsSync(imagesPath)) {
      console.log('📁 Creating images directory...');
      fs.mkdirSync(imagesPath, { recursive: true });
    } else {
      console.log('✅ Images directory exists');
    }
    
    cb(null, imagesPath);
  },
  filename: function (req, file, cb) {
    console.log('📝 Multer filename function called');
    console.log('   - req.body:', req.body);
    console.log('   - file.originalname:', file.originalname);
    
    // Use the original name or the provided imageName
    const imageName = req.body.imageName || file.originalname;
    const extension = path.extname(file.originalname) || '.png';
    const finalName = imageName.endsWith(extension) ? imageName : imageName + extension;
    
    console.log('   - Final filename will be:', finalName);
    cb(null, finalName);
  }
});

const upload = multer({ 
  storage: storage,
  limits: {
    fileSize: 10 * 1024 * 1024 // 10MB limit
  },
  fileFilter: function (req, file, cb) {
    console.log('🔍 Multer fileFilter called');
    console.log('   - mimetype:', file.mimetype);
    console.log('   - fieldname:', file.fieldname);
    
    // Accept all image types
    if (file.mimetype.startsWith('image/') || file.fieldname === 'image') {
      console.log('✅ File accepted by filter');
      cb(null, true);
    } else {
      console.log('❌ File rejected by filter');
      cb(new Error('Only image files are allowed'), false);
    }
  }
});

// Root endpoint
app.get('/', (req, res) => {
  res.json({ 
    message: 'Treasure Images Server Running', 
    endpoints: {
      uploadImage: 'POST /upload-treasure-image',
      uploadConfig: 'POST /upload-web-config',
      deleteImage: 'POST /delete-image',
      cleanupImages: 'POST /cleanup-images',
      getImages: 'GET /images',
      getConfig: 'GET /config',
      listImages: 'GET /images-list',
      serverData: 'GET /ServerData (Unity Addressables)'
    }
  });
});

// Upload treasure image endpoint
app.post('/upload-treasure-image', (req, res) => {
  console.log('🚀 Upload endpoint hit');
  
  // Use multer with error handling
  upload.single('image')(req, res, async (err) => {
    if (err) {
      console.error('❌ Multer error:', err);
      console.error('Error type:', err.constructor.name);
      console.error('Error message:', err.message);
      return res.status(400).json({ 
        success: false, 
        error: 'File upload failed',
        details: err.message
      });
    }
    try {
      console.log('=== IMAGE UPLOAD REQUEST ===');
      console.log('Headers:', req.headers);
      console.log('Body:', req.body);
      console.log('File info:', req.file ? {
        filename: req.file.filename,
        originalname: req.file.originalname,
        size: req.file.size,
        mimetype: req.file.mimetype,
        path: req.file.path
      } : 'No file');

      if (!req.file) {
        console.error('❌ No image file provided in request');
        return res.status(400).json({ success: false, error: 'No image file provided' });
      }

      const { imageName, latitude, longitude, validationScore } = req.body;

      console.log('✅ Image uploaded successfully:', {
        filename: req.file.filename,
        size: req.file.size,
        imageName,
        latitude,
        longitude,
        validationScore
      });

      // Check if file actually exists on disk
      const fs = require('fs');
      if (fs.existsSync(req.file.path)) {
        console.log('✅ File saved to disk at:', req.file.path);
      } else {
        console.error('❌ File not found on disk at:', req.file.path);
      }

      // DON'T update config here - let the web config endpoint handle it
      // The frontend will call /api/update-web-config after successful image upload
      console.log('📝 Image upload complete, waiting for config update from frontend');

      // Return success response
      const response = {
        success: true,
        filename: req.file.filename,
        url: `/images/${req.file.filename}`,
        imageName,
        validationScore: validationScore ? parseInt(validationScore) : null
      };
      
      console.log('📤 Sending response:', response);
      res.json(response);

    } catch (error) {
      console.error('❌ Image upload error:', error);
      console.error('Error stack:', error.stack);
      res.status(500).json({ 
        success: false, 
        error: 'Failed to upload image',
        details: error.message
      });
    }
  });
});

// Delete image endpoint
app.post('/delete-image', async (req, res) => {
  try {
    console.log('🗑️  Image delete request received');
    console.log('Request body:', req.body);

    const { fileName } = req.body;
    
    if (!fileName) {
      return res.status(400).json({ success: false, error: 'No fileName provided' });
    }

    const imagePath = path.join(__dirname, 'images', fileName);
    console.log('Attempting to delete image at:', imagePath);

    // Check if file exists
    const fs = require('fs');
    if (!fs.existsSync(imagePath)) {
      console.log('⚠️  Image file not found, considering it already deleted');
      return res.json({ success: true, message: 'Image file not found (may already be deleted)' });
    }

    // Delete the file
    await fs.promises.unlink(imagePath);
    console.log('✅ Image file deleted successfully:', fileName);

    res.json({
      success: true,
      message: 'Image deleted successfully',
      fileName: fileName
    });

  } catch (error) {
    console.error('❌ Image delete error:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to delete image',
      details: error.message
    });
  }
});

// Upload web config endpoint
app.post('/upload-web-config', async (req, res) => {
  try {
    console.log('=== WEB CONFIG UPDATE REQUEST ===');
    console.log('New config data:', req.body);

    const configPath = path.join(__dirname, 'config.json');
    
    // Read existing config if it exists
    let existingConfig = { images: [], lastUpdated: '', totalTreasures: 0 };
    try {
      if (require('fs').existsSync(configPath)) {
        const existingData = await fs.readFile(configPath, 'utf8');
        existingConfig = JSON.parse(existingData);
        console.log('📖 Existing config loaded:', {
          existingTreasures: existingConfig.images?.length || 0,
          lastUpdated: existingConfig.lastUpdated
        });
      } else {
        console.log('📝 No existing config found, creating new one');
      }
    } catch (readError) {
      console.warn('⚠️  Failed to read existing config, starting fresh:', readError.message);
      existingConfig = { images: [], lastUpdated: '', totalTreasures: 0 };
    }

    // Use the complete config from the request (frontend manages the full state)
    const newConfig = {
      images: req.body.images || [],
      lastUpdated: req.body.lastUpdated || new Date().toISOString(),
      totalTreasures: req.body.totalTreasures || (req.body.images ? req.body.images.length : 0)
    };

    console.log('💾 Writing new config:', {
      newTreasures: newConfig.images.length,
      treasureNames: newConfig.images.map(img => img.clueName),
      operation: 'CONFIG_UPDATE'
    });
    
    // Write the updated config data to config.json
    await fs.writeFile(configPath, JSON.stringify(newConfig, null, 2), 'utf8');
    
    console.log('✅ Config.json updated successfully');

    res.json({
      success: true,
      message: 'Web config updated successfully',
      treasureCount: newConfig.images.length,
      treasures: newConfig.images.map(img => ({
        name: img.clueName,
        file: img.fileName
      }))
    });

  } catch (error) {
    console.error('❌ Config upload error:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to update config',
      details: error.message 
    });
  }
});

// Get current config
app.get('/config', async (req, res) => {
  try {
    const configPath = path.join(__dirname, 'config.json');
    const configData = await fs.readFile(configPath, 'utf8');
    const config = JSON.parse(configData);
    
    res.json(config);
  } catch (error) {
    console.error('Failed to read config:', error);
    res.status(500).json({ success: false, error: 'Failed to read config' });
  }
});

// Cleanup orphaned images (images not in config)
app.post('/cleanup-images', async (req, res) => {
  try {
    console.log('🧹 Starting image cleanup...');
    
    const configPath = path.join(__dirname, 'config.json');
    const imagesDir = path.join(__dirname, 'images');
    
    // Read current config
    let config = { images: [] };
    try {
      const configData = await fs.readFile(configPath, 'utf8');
      config = JSON.parse(configData);
    } catch (error) {
      console.error('Failed to read config:', error);
      return res.status(500).json({ success: false, error: 'Failed to read config file' });
    }
    
    // Get list of files that should exist according to config
    const expectedFiles = new Set((config.images || []).map(img => img.fileName));
    console.log('📋 Expected files from config:', Array.from(expectedFiles));
    
    // Get list of actual files in images directory
    const actualFiles = await fs.readdir(imagesDir);
    const imageFiles = actualFiles.filter(file => /\.(jpg|jpeg|png|gif)$/i.test(file));
    console.log('📁 Actual files in directory:', imageFiles);
    
    // Find orphaned files (files not in config)
    const orphanedFiles = imageFiles.filter(file => !expectedFiles.has(file));
    console.log('🗑️ Orphaned files to delete:', orphanedFiles);
    
    // Delete orphaned files
    let deletedCount = 0;
    const deletionResults = [];
    
    for (const file of orphanedFiles) {
      try {
        const filePath = path.join(imagesDir, file);
        await fs.unlink(filePath);
        console.log(`✅ Deleted orphaned file: ${file}`);
        deletedCount++;
        deletionResults.push({ file, deleted: true });
      } catch (error) {
        console.error(`❌ Failed to delete ${file}:`, error);
        deletionResults.push({ file, deleted: false, error: error.message });
      }
    }
    
    res.json({
      success: true,
      message: `Cleanup complete. Deleted ${deletedCount} orphaned files.`,
      expectedFiles: Array.from(expectedFiles),
      actualFiles: imageFiles,
      orphanedFiles: orphanedFiles,
      deletionResults: deletionResults,
      deletedCount: deletedCount
    });
    
  } catch (error) {
    console.error('❌ Cleanup error:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to cleanup images',
      details: error.message 
    });
  }
});

// List uploaded images
app.get('/images-list', async (req, res) => {
  try {
    const imagesDir = path.join(__dirname, 'images');
    const files = await fs.readdir(imagesDir);
    const imageFiles = files.filter(file => /\.(jpg|jpeg|png|gif)$/i.test(file));
    
    res.json({
      success: true,
      images: imageFiles.map(filename => ({
        filename,
        url: `/images/${filename}`
      }))
    });
  } catch (error) {
    console.error('Failed to list images:', error);
    res.status(500).json({ success: false, error: 'Failed to list images' });
  }
});

// Error handling middleware
app.use((error, req, res, next) => {
  console.error('Server error:', error);
  res.status(500).json({ success: false, error: 'Internal server error' });
});

// Start server
app.listen(PORT, () => {
  console.log(`🚀 Treasure Images Server running on http://localhost:${PORT}`);
  console.log(`📁 Serving images from: ${path.join(__dirname, 'images')}`);
  console.log(`📄 Config file: ${path.join(__dirname, 'config.json')}`);
  console.log('');
  console.log('Available endpoints:');
  console.log('  POST /upload-treasure-image  - Upload new treasure images');
  console.log('  POST /upload-web-config      - Update treasure configuration');
  console.log('  GET  /config                 - Get current configuration');
  console.log('  GET  /images-list            - List all uploaded images');
  console.log('  GET  /images/<filename>      - Serve image files');
  console.log('  GET  /ServerData/*           - Unity Addressables assets');
  console.log('');
  console.log('💡 Don\'t forget to:');
  console.log('   1. Run `npm install` if you haven\'t already');
  console.log('   2. Make sure ngrok is pointing to this server');
  console.log('   3. Update your client config with the ngrok URL');
});